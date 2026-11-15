// QC-Plugin 99-2 "Kamera" – Anbindung an Media Foundation (Windows), nur was der Decoder braucht.
// Die Schnittstellen sind von Hand nach mfobjects.h / mftransform.h (Windows SDK) abgebildet; nicht benutzte
// Methoden stehen als Platzhalter "SlotN" da, weil nur ihre Reihenfolge zählt.
using System;
using System.Runtime.InteropServices;

namespace QcKamera.Media
{
    static class MF
    {
        public const int Version = 0x00020070;

        public const int E_NOTACCEPTING = unchecked((int)0xC00D36B5);
        public const int E_NEED_MORE_INPUT = unchecked((int)0xC00D6D72);
        public const int E_STREAM_CHANGE = unchecked((int)0xC00D6D61);
        public const int E_NO_MORE_TYPES = unchecked((int)0xC00D36B9);

        public const int MessageFlush = 0, MessageDrain = 1, MessageBeginStreaming = 0x10000000, MessageStartOfStream = 0x10000003;
        public const int OutputProvidesSamples = 0x100, OutputCanProvideSamples = 0x200;

        public static readonly Guid MajorType = new Guid("48eba18e-f8c9-4687-bf11-0a74c9f96a8f");
        public static readonly Guid Subtype = new Guid("f7e34c9a-42e8-4714-b74b-cb29d72c35e5");
        public static readonly Guid FrameSize = new Guid("1652c33d-d6b2-4012-b834-72030849a37d");
        public static readonly Guid FrameRate = new Guid("c459a2e8-3d2c-4e44-b132-fee5156c7bb0");
        public static readonly Guid PixelAspect = new Guid("c6376a1e-8d0a-4027-be45-6d9a0ad39bb6");
        public static readonly Guid InterlaceMode = new Guid("e2724bb8-e676-4806-b4b2-a8d6efb44ccd");
        public static readonly Guid AvgBitrate = new Guid("20332624-fb0d-4d9e-bd0d-cbf6786c102e");
        public static readonly Guid DefaultStride = new Guid("644b4e48-1e02-4516-b0eb-c01ca9d49ac6");
        public static readonly Guid MinDisplayAperture = new Guid("d7388766-18fe-48c6-a177-ee894867c8c4");
        public static readonly Guid Mpeg2Profile = new Guid("ad76a80b-2d5c-4e0b-b375-64e520137036");
        public static readonly Guid LowLatency = new Guid("9c27891a-ed7a-40e1-88e8-b22727a024ee");

        public static readonly Guid Video = new Guid("73646976-0000-0010-8000-00AA00389B71");
        public static readonly Guid H264 = new Guid("34363248-0000-0010-8000-00AA00389B71");
        public static readonly Guid Hevc = new Guid("43564548-0000-0010-8000-00AA00389B71");
        public static readonly Guid Nv12 = new Guid("3231564E-0000-0010-8000-00AA00389B71");

        public static readonly Guid ClsidH264Decoder = new Guid("62CE7E72-4C71-4D20-B15D-452831A87D9D");
        public static readonly Guid ClsidHevcDecoder = new Guid("420A51A3-D605-430C-B4FC-45274FA6C562");
        public static readonly Guid ClsidH264Encoder = new Guid("6CA50344-051A-4DED-9779-A43305165E35");

        [DllImport("mfplat.dll", ExactSpelling = true)] public static extern int MFStartup(int version, int flags);
        [DllImport("mfplat.dll", ExactSpelling = true)] public static extern int MFShutdown();
        [DllImport("mfplat.dll", ExactSpelling = true)] public static extern int MFCreateMediaType(out IMFMediaType type);
        [DllImport("mfplat.dll", ExactSpelling = true)] public static extern int MFCreateSample(out IMFSample sample);
        [DllImport("mfplat.dll", ExactSpelling = true)] public static extern int MFCreateMemoryBuffer(int max, out IMFMediaBuffer buffer);

        public static void Check(int hr, string what)
        {
            if (hr < 0) throw new InvalidOperationException(what + " (0x" + hr.ToString("X8") + ")");
        }

        public static long Pack(int hi, int lo) { return ((long)hi << 32) | (uint)lo; }

        public static IMFMediaType VideoType(Guid subtype)
        {
            IMFMediaType t;
            Check(MFCreateMediaType(out t), "MFCreateMediaType");
            Guid k = MajorType, v = Video;
            t.SetGUID(ref k, ref v);
            k = Subtype;
            t.SetGUID(ref k, ref subtype);
            return t;
        }

        public static IMFTransform Create(Guid clsid, string name)
        {
            Type t = Type.GetTypeFromCLSID(clsid, false);
            if (t == null) throw new InvalidOperationException(name + " ist auf diesem Rechner nicht vorhanden");
            try { return (IMFTransform)Activator.CreateInstance(t); }
            catch (Exception ex) { throw new InvalidOperationException(name + " ist auf diesem Rechner nicht vorhanden (" + ex.Message + ")"); }
        }

        public static void Release(object o)
        {
            try { if (o != null && Marshal.IsComObject(o)) Marshal.ReleaseComObject(o); } catch { }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    struct MftOutputStreamInfo { public int Flags, Size, Alignment; }

    [StructLayout(LayoutKind.Sequential)]
    struct MftOutputDataBuffer { public int StreamId; public IntPtr Sample; public int Status; public IntPtr Events; }

    [ComImport, Guid("2CD2D921-C447-44A7-A13C-4ADABFC247E3"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMFAttributes
    {
        void Slot1(); void Slot2(); void Slot3(); void Slot4();
        [PreserveSig] int GetUINT32([In] ref Guid key, out int value);
        [PreserveSig] int GetUINT64([In] ref Guid key, out long value);
        void Slot7();
        [PreserveSig] int GetGUID([In] ref Guid key, out Guid value);
        void Slot9(); void Slot10(); void Slot11(); void Slot12();
        [PreserveSig] int GetBlob([In] ref Guid key, [Out, MarshalAs(UnmanagedType.LPArray)] byte[] buf, int size, out int written);
        void Slot14(); void Slot15(); void Slot16(); void Slot17(); void Slot18();
        void SetUINT32([In] ref Guid key, int value);
        void SetUINT64([In] ref Guid key, long value);
        void Slot21();
        void SetGUID([In] ref Guid key, [In] ref Guid value);
        void Slot23(); void Slot24(); void Slot25(); void Slot26(); void Slot27(); void Slot28(); void Slot29(); void Slot30();
    }

    [ComImport, Guid("44AE0FA8-EA31-4109-8D2E-4CAE4997C555"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMFMediaType
    {
        void Slot1(); void Slot2(); void Slot3(); void Slot4();
        [PreserveSig] int GetUINT32([In] ref Guid key, out int value);
        [PreserveSig] int GetUINT64([In] ref Guid key, out long value);
        void Slot7();
        [PreserveSig] int GetGUID([In] ref Guid key, out Guid value);
        void Slot9(); void Slot10(); void Slot11(); void Slot12();
        [PreserveSig] int GetBlob([In] ref Guid key, [Out, MarshalAs(UnmanagedType.LPArray)] byte[] buf, int size, out int written);
        void Slot14(); void Slot15(); void Slot16(); void Slot17(); void Slot18();
        void SetUINT32([In] ref Guid key, int value);
        void SetUINT64([In] ref Guid key, long value);
        void Slot21();
        void SetGUID([In] ref Guid key, [In] ref Guid value);
        void Slot23(); void Slot24(); void Slot25(); void Slot26(); void Slot27(); void Slot28(); void Slot29(); void Slot30();
        void Slot31(); void Slot32(); void Slot33(); void Slot34(); void Slot35();
    }

    [ComImport, Guid("C40A00F2-B93A-4D80-AE8C-5A1C634F58E4"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMFSample
    {
        void Slot1(); void Slot2(); void Slot3(); void Slot4(); void Slot5(); void Slot6(); void Slot7(); void Slot8(); void Slot9(); void Slot10();
        void Slot11(); void Slot12(); void Slot13(); void Slot14(); void Slot15(); void Slot16(); void Slot17(); void Slot18(); void Slot19(); void Slot20();
        void Slot21(); void Slot22(); void Slot23(); void Slot24(); void Slot25(); void Slot26(); void Slot27(); void Slot28(); void Slot29(); void Slot30();
        void Slot31(); void Slot32(); void Slot33();
        void SetSampleTime(long time);
        void Slot35();
        void SetSampleDuration(long duration);
        void Slot37(); void Slot38();
        void ConvertToContiguousBuffer(out IMFMediaBuffer buffer);
        void AddBuffer(IMFMediaBuffer buffer);
        void Slot41(); void Slot42(); void Slot43(); void Slot44();
    }

    [ComImport, Guid("045FA593-8799-42B8-BC8D-8968C6453507"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMFMediaBuffer
    {
        void Lock(out IntPtr data, out int maxLength, out int currentLength);
        void Unlock();
        void GetCurrentLength(out int length);
        void SetCurrentLength(int length);
        void GetMaxLength(out int length);
    }

    [ComImport, Guid("BF94C121-5B05-4E6F-8000-BA598961414D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMFTransform
    {
        void Slot1(); void Slot2(); void Slot3(); void Slot4();
        void GetOutputStreamInfo(int streamId, out MftOutputStreamInfo info);
        [PreserveSig] int GetAttributes(out IMFAttributes attributes);
        void Slot7(); void Slot8(); void Slot9(); void Slot10(); void Slot11();
        [PreserveSig] int GetOutputAvailableType(int streamId, int index, out IMFMediaType type);
        [PreserveSig] int SetInputType(int streamId, IMFMediaType type, int flags);
        [PreserveSig] int SetOutputType(int streamId, IMFMediaType type, int flags);
        void Slot15();
        [PreserveSig] int GetOutputCurrentType(int streamId, out IMFMediaType type);
        void Slot17(); void Slot18(); void Slot19(); void Slot20();
        [PreserveSig] int ProcessMessage(int message, IntPtr param);
        [PreserveSig] int ProcessInput(int streamId, IMFSample sample, int flags);
        [PreserveSig] int ProcessOutput(int flags, int count, ref MftOutputDataBuffer buffer, out int status);
    }
}
