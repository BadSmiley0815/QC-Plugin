// Erzeugt für die Testprogramme einen H.264-Strom mit bekanntem Inhalt (ohne Kamera, ohne fremde Programme):
// linke Hälfte rot, rechte Hälfte blau, unten ein weißer Balken, der je Bild 8 Punkte weiterwandert.
// Kodiert wird mit dem H.264-Encoder von Windows.
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using QcKamera.Media;

static class TestQuelle
{
    public const int BarWidth = 24, BarStep = 8;

    public static int BarX(int frame, int w) { return (frame * BarStep) % (w - BarWidth); }

    static void Rgb(int x, int y, int frame, int w, int h, out int r, out int g, out int b)
    {
        int bx = BarX(frame, w);
        if (y >= h * 3 / 4 && x >= bx && x < bx + BarWidth) { r = g = b = 255; return; }
        if (x < w / 2) { r = 255; g = 0; b = 0; } else { r = 0; g = 0; b = 255; }
    }

    public static byte[] Frame(int frame, int w, int h)
    {
        byte[] n = new byte[w * h * 3 / 2];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int r, g, b;
                Rgb(x, y, frame, w, h, out r, out g, out b);
                n[y * w + x] = (byte)(((66 * r + 129 * g + 25 * b + 128) >> 8) + 16);
                if ((x & 1) == 0 && (y & 1) == 0)
                {
                    int o = w * h + (y / 2) * w + x;
                    n[o] = (byte)(((-38 * r - 74 * g + 112 * b + 128) >> 8) + 128);
                    n[o + 1] = (byte)(((112 * r - 94 * g - 18 * b + 128) >> 8) + 128);
                }
            }
        return n;
    }

    /// <summary>Liefert je Bild die NAL-Einheiten mit Startcodes.</summary>
    public static List<byte[]> Encode(int w, int h, int frames, int fps)
    {
        MF.Check(MF.MFStartup(MF.Version, 0), "MFStartup");
        IMFTransform enc = MF.Create(MF.ClsidH264Encoder, "Der H.264-Encoder von Windows");
        List<byte[]> result = new List<byte[]>();
        Guid k;

        IMFMediaType ot = MF.VideoType(MF.H264);
        k = MF.AvgBitrate; ot.SetUINT32(ref k, 1500000);
        k = MF.FrameSize; ot.SetUINT64(ref k, MF.Pack(w, h));
        k = MF.FrameRate; ot.SetUINT64(ref k, MF.Pack(fps, 1));
        k = MF.InterlaceMode; ot.SetUINT32(ref k, 2);
        k = MF.PixelAspect; ot.SetUINT64(ref k, MF.Pack(1, 1));
        k = MF.Mpeg2Profile; ot.SetUINT32(ref k, 66);           // Baseline: keine B-Bilder, wie bei Kameras üblich
        MF.Check(enc.SetOutputType(0, ot, 0), "Encoder: Ausgangsformat");

        IMFMediaType it = MF.VideoType(MF.Nv12);
        k = MF.FrameSize; it.SetUINT64(ref k, MF.Pack(w, h));
        k = MF.FrameRate; it.SetUINT64(ref k, MF.Pack(fps, 1));
        k = MF.InterlaceMode; it.SetUINT32(ref k, 2);
        k = MF.PixelAspect; it.SetUINT64(ref k, MF.Pack(1, 1));
        MF.Check(enc.SetInputType(0, it, 0), "Encoder: Eingangsformat");

        MftOutputStreamInfo info;
        enc.GetOutputStreamInfo(0, out info);
        bool provides = (info.Flags & MF.OutputProvidesSamples) != 0;
        enc.ProcessMessage(MF.MessageBeginStreaming, IntPtr.Zero);
        enc.ProcessMessage(MF.MessageStartOfStream, IntPtr.Zero);

        long dur = 10000000L / fps;
        for (int f = 0; f < frames; f++)
        {
            byte[] nv = Frame(f, w, h);
            IMFSample s; IMFMediaBuffer b;
            MF.Check(MF.MFCreateSample(out s), "MFCreateSample");
            MF.Check(MF.MFCreateMemoryBuffer(nv.Length, out b), "MFCreateMemoryBuffer");
            IntPtr p; int max, cur;
            b.Lock(out p, out max, out cur);
            Marshal.Copy(nv, 0, p, nv.Length);
            b.Unlock();
            b.SetCurrentLength(nv.Length);
            s.AddBuffer(b);
            s.SetSampleTime(f * dur);
            s.SetSampleDuration(dur);
            MF.Check(enc.ProcessInput(0, s, 0), "Encoder: ProcessInput");
            MF.Release(b); MF.Release(s);
            while (Pull(enc, provides, Math.Max(info.Size, w * h * 2), result)) { }
        }
        enc.ProcessMessage(MF.MessageDrain, IntPtr.Zero);
        while (Pull(enc, provides, Math.Max(info.Size, w * h * 2), result)) { }
        MF.Release(ot); MF.Release(it); MF.Release(enc);
        return result;
    }

    static bool Pull(IMFTransform enc, bool provides, int size, List<byte[]> result)
    {
        MftOutputDataBuffer o = new MftOutputDataBuffer();
        IMFSample s = null; IMFMediaBuffer b = null;
        IntPtr own = IntPtr.Zero;
        if (!provides)
        {
            MF.Check(MF.MFCreateSample(out s), "MFCreateSample");
            MF.Check(MF.MFCreateMemoryBuffer(size, out b), "MFCreateMemoryBuffer");
            s.AddBuffer(b);
            own = Marshal.GetComInterfaceForObject(s, typeof(IMFSample));
            o.Sample = own;
        }
        int status;
        int hr = enc.ProcessOutput(0, 1, ref o, out status);
        if (own != IntPtr.Zero) Marshal.Release(own);
        if (o.Events != IntPtr.Zero) Marshal.Release(o.Events);
        try
        {
            if (hr == MF.E_NEED_MORE_INPUT) return false;
            if (hr == MF.E_STREAM_CHANGE) return true;
            MF.Check(hr, "Encoder: ProcessOutput");
            if (provides)
            {
                s = (IMFSample)Marshal.GetObjectForIUnknown(o.Sample);
                Marshal.Release(o.Sample);
            }
            IMFMediaBuffer c;
            s.ConvertToContiguousBuffer(out c);
            IntPtr p; int max, cur;
            c.Lock(out p, out max, out cur);
            byte[] au = new byte[cur];
            Marshal.Copy(p, au, 0, cur);
            c.Unlock();
            MF.Release(c);
            if (cur > 0) result.Add(au);
            return true;
        }
        finally { MF.Release(b); MF.Release(s); }
    }

    /// <summary>NAL-Typen eines Bildes, z. B. "7 8 5".</summary>
    public static string NalTypes(byte[] au)
    {
        List<string> t = new List<string>();
        for (int i = 0; i + 3 < au.Length; i++)
            if (au[i] == 0 && au[i + 1] == 0 && au[i + 2] == 1) { t.Add((au[i + 3] & 0x1F).ToString()); i += 3; }
        return string.Join(" ", t.ToArray());
    }
}
