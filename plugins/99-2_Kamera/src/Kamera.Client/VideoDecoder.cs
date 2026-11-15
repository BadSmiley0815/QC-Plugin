// QC-Plugin 99-2 "Kamera" – H.264 (und H.265, wenn die HEVC-Erweiterung installiert ist) mit dem Decoder von Windows.
// Eingabe: je Bild die NAL-Einheiten mit Startcodes (Annex B). Ausgabe: NV12, bei Bedarf nach BGRA umgerechnet.
// Ein Decoder gehört dem Thread, der ihn angelegt hat.
using System;
using System.Runtime.InteropServices;

namespace QcKamera.Media
{
    delegate void FrameHandler(VideoDecoder decoder);

    sealed class VideoDecoder : IDisposable
    {
        readonly IMFTransform _mft;
        readonly bool _hevc;
        IMFSample _outSample;
        IMFMediaBuffer _outBuffer;
        bool _providesSamples, _started;
        long _time;

        /// <summary>Letztes Bild: Helligkeit (Stride × CodedHeight), danach Farbe (U,V abwechselnd, halbe Höhe).</summary>
        public byte[] Nv12 = new byte[0];
        public int Stride, CodedHeight;
        /// <summary>Sichtbarer Ausschnitt des Bildes.</summary>
        public int CropX, CropY, Width, Height;
        public int Frames;

        public VideoDecoder(bool hevc)
        {
            _hevc = hevc;
            MF.Check(MF.MFStartup(MF.Version, 0), "MFStartup");
            _mft = hevc ? MF.Create(MF.ClsidHevcDecoder, "Der H.265-Decoder (HEVC-Videoerweiterung)")
                        : MF.Create(MF.ClsidH264Decoder, "Der H.264-Decoder von Windows");
            IMFAttributes a;
            if (_mft.GetAttributes(out a) >= 0 && a != null)
            {
                Guid k = MF.LowLatency;
                try { a.SetUINT32(ref k, 1); } catch { }     // jedes Bild sofort ausgeben, nicht auf Folgebilder warten
                MF.Release(a);
            }
            IMFMediaType t = MF.VideoType(hevc ? MF.Hevc : MF.H264);
            MF.Check(_mft.SetInputType(0, t, 0), "Decoder: Eingangsformat");
            MF.Release(t);
            SelectOutput();
        }

        /// <summary>Wählt NV12 als Ausgangsformat und liest Bildgröße und Ausschnitt.</summary>
        void SelectOutput()
        {
            for (int i = 0; ; i++)
            {
                IMFMediaType t;
                int hr = _mft.GetOutputAvailableType(0, i, out t);
                if (hr < 0) throw new InvalidOperationException("Decoder bietet kein NV12 an (0x" + hr.ToString("X8") + ")");
                Guid k = MF.Subtype, sub;
                t.GetGUID(ref k, out sub);
                if (sub != MF.Nv12) { MF.Release(t); continue; }
                MF.Check(_mft.SetOutputType(0, t, 0), "Decoder: Ausgangsformat");
                long size;
                k = MF.FrameSize;
                if (t.GetUINT64(ref k, out size) >= 0)
                {
                    int w = (int)(size >> 32), h = (int)size, stride;
                    k = MF.DefaultStride;
                    if (t.GetUINT32(ref k, out stride) < 0 || stride < w) stride = w;
                    Stride = stride; CodedHeight = h;
                    CropX = 0; CropY = 0; Width = w; Height = h;
                    byte[] area = new byte[16];          // MFVideoArea: OffsetX, OffsetY (je 2 Bytes Bruchteil + 2 Bytes Wert), Breite, Höhe
                    int n;
                    k = MF.MinDisplayAperture;
                    if (t.GetBlob(ref k, area, 16, out n) >= 0 && n == 16)
                    {
                        int cw = BitConverter.ToInt32(area, 8), ch = BitConverter.ToInt32(area, 12);
                        int cx = BitConverter.ToInt16(area, 2), cy = BitConverter.ToInt16(area, 6);
                        if (cw > 0 && ch > 0 && cx >= 0 && cy >= 0 && cx + cw <= w && cy + ch <= h) { CropX = cx; CropY = cy; Width = cw; Height = ch; }
                    }
                }
                MF.Release(t);
                break;
            }
            MftOutputStreamInfo info;
            _mft.GetOutputStreamInfo(0, out info);
            _providesSamples = (info.Flags & MF.OutputProvidesSamples) != 0;
            MF.Release(_outBuffer); MF.Release(_outSample);
            _outBuffer = null; _outSample = null;
            if (!_providesSamples)
            {
                int size = Math.Max(info.Size, Stride * CodedHeight * 3 / 2);
                if (size <= 0) size = 1920 * 1088 * 3 / 2;
                MF.Check(MF.MFCreateSample(out _outSample), "MFCreateSample");
                MF.Check(MF.MFCreateMemoryBuffer(size, out _outBuffer), "MFCreateMemoryBuffer");
                _outSample.AddBuffer(_outBuffer);
            }
        }

        /// <summary>Gibt ein Bild (alle NAL-Einheiten mit Startcodes) in den Decoder; ruft onFrame für jedes fertige Bild.</summary>
        public void Decode(byte[] data, int length, FrameHandler onFrame)
        {
            if (!_started)
            {
                _mft.ProcessMessage(MF.MessageBeginStreaming, IntPtr.Zero);
                _mft.ProcessMessage(MF.MessageStartOfStream, IntPtr.Zero);
                _started = true;
            }
            IMFSample s;
            IMFMediaBuffer b;
            MF.Check(MF.MFCreateSample(out s), "MFCreateSample");
            MF.Check(MF.MFCreateMemoryBuffer(length, out b), "MFCreateMemoryBuffer");
            try
            {
                IntPtr p; int max, cur;
                b.Lock(out p, out max, out cur);
                Marshal.Copy(data, 0, p, length);
                b.Unlock();
                b.SetCurrentLength(length);
                s.AddBuffer(b);
                s.SetSampleTime(_time);
                s.SetSampleDuration(400000);
                _time += 400000;
                for (int attempt = 0; attempt < 3; attempt++)
                {
                    int hr = _mft.ProcessInput(0, s, 0);
                    if (hr == MF.E_NOTACCEPTING) { if (!Pull(onFrame)) break; continue; }
                    MF.Check(hr, "Decoder: ProcessInput");
                    break;
                }
                while (Pull(onFrame)) { }
            }
            finally { MF.Release(b); MF.Release(s); }
        }

        /// <summary>Holt ein fertiges Bild ab. false = der Decoder braucht erst neue Daten.</summary>
        bool Pull(FrameHandler onFrame)
        {
            MftOutputDataBuffer o = new MftOutputDataBuffer();
            IntPtr own = IntPtr.Zero;
            if (!_providesSamples)
            {
                _outBuffer.SetCurrentLength(0);
                own = Marshal.GetComInterfaceForObject(_outSample, typeof(IMFSample));
                o.Sample = own;
            }
            int status, hr;
            try { hr = _mft.ProcessOutput(0, 1, ref o, out status); }
            finally { if (own != IntPtr.Zero) Marshal.Release(own); }
            if (o.Events != IntPtr.Zero) Marshal.Release(o.Events);
            if (hr == MF.E_NEED_MORE_INPUT) return false;
            if (hr == MF.E_STREAM_CHANGE) { SelectOutput(); return true; }
            MF.Check(hr, "Decoder: ProcessOutput");

            IMFSample sample = _outSample;
            if (_providesSamples)
            {
                if (o.Sample == IntPtr.Zero) return true;
                sample = (IMFSample)Marshal.GetObjectForIUnknown(o.Sample);
                Marshal.Release(o.Sample);
            }
            IMFMediaBuffer buf = null;
            try
            {
                sample.ConvertToContiguousBuffer(out buf);
                IntPtr p; int max, cur;
                buf.Lock(out p, out max, out cur);
                try
                {
                    if (Nv12.Length < cur) Nv12 = new byte[cur];
                    Marshal.Copy(p, Nv12, 0, cur);
                }
                finally { buf.Unlock(); }
                if (cur >= Stride * CodedHeight * 3 / 2 && Width > 0)
                {
                    Frames++;
                    if (onFrame != null) onFrame(this);
                }
            }
            finally
            {
                MF.Release(buf);
                if (_providesSamples) MF.Release(sample);
            }
            return true;
        }

        /// <summary>Größe des Bildes, das ToBgra bei diesem Teiler liefert.</summary>
        public void ScaledSize(int divisor, out int w, out int h)
        {
            w = Math.Max(1, Width / divisor);
            h = Math.Max(1, Height / divisor);
        }

        /// <summary>Teiler, mit dem das Bild noch mindestens so groß wie die Anzeige bleibt (1, 2, 3 oder 4).</summary>
        public int DivisorFor(double targetW, double targetH)
        {
            if (targetW < 1 || targetH < 1) return 1;
            int d = (int)Math.Floor(Math.Min(Width / targetW, Height / targetH));
            return d < 1 ? 1 : d > 4 ? 4 : d;
        }

        /// <summary>Rechnet das letzte Bild nach BGRA um (4 Bytes je Bildpunkt); divisor mittelt Blöcke und verkleinert.</summary>
        public void ToBgra(int divisor, ref byte[] dst)
        {
            int w, h;
            ScaledSize(divisor, out w, out h);
            if (dst == null || dst.Length != w * h * 4) dst = new byte[w * h * 4];
            byte[] src = Nv12;
            int uvBase = Stride * CodedHeight;
            bool hd = Height > 576;                       // BT.709 für HD, sonst BT.601; jeweils begrenzter Wertebereich
            int rv = hd ? 459 : 409, gu = hd ? 55 : 100, gv = hd ? 136 : 208, bu = hd ? 541 : 516;
            int area = divisor * divisor, di = 0;
            for (int y = 0; y < h; y++)
            {
                int sy = CropY + y * divisor;
                int rowY = sy * Stride, rowUv = uvBase + (sy / 2) * Stride;
                for (int x = 0; x < w; x++)
                {
                    int sx = CropX + x * divisor;
                    int lum;
                    if (divisor == 1) lum = src[rowY + sx];
                    else
                    {
                        lum = 0;
                        for (int j = 0; j < divisor; j++)
                        {
                            int q = rowY + j * Stride + sx;
                            for (int i = 0; i < divisor; i++) lum += src[q + i];
                        }
                        lum /= area;
                    }
                    int uv = rowUv + (sx & ~1);
                    int c = 298 * (lum - 16) + 128, d = src[uv] - 128, e = src[uv + 1] - 128;
                    int r = (c + rv * e) >> 8, g = (c - gu * d - gv * e) >> 8, b = (c + bu * d) >> 8;
                    dst[di] = (byte)(b < 0 ? 0 : b > 255 ? 255 : b);
                    dst[di + 1] = (byte)(g < 0 ? 0 : g > 255 ? 255 : g);
                    dst[di + 2] = (byte)(r < 0 ? 0 : r > 255 ? 255 : r);
                    dst[di + 3] = 255;
                    di += 4;
                }
            }
        }

        public void Dispose()
        {
            MF.Release(_outBuffer); MF.Release(_outSample); MF.Release(_mft);
            try { MF.MFShutdown(); } catch { }
        }
    }
}
