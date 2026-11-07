using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace SCPSLAlert
{
    internal sealed class DumpModule
    {
        public ulong Base;
        public uint Size;
        public string Name = "";

        public string FileName
        {
            get
            {
                try { return Path.GetFileName(Name); }
                catch { return Name; }
            }
        }
    }

    internal sealed class DumpFrame
    {
        public ulong Address;
        public string Module = "";
        public ulong ModuleBase;
        public uint Rva;

        public string Text
        {
            get { return Module + "+0x" + Rva.ToString("X", CultureInfo.InvariantCulture); }
        }
    }

    internal sealed class MiniDump
    {
        public readonly List<DumpModule> Modules = new List<DumpModule>();
        public readonly List<DumpFrame> Frames = new List<DumpFrame>();
        public uint ExceptionCode;
        public ulong ExceptionAddress;
        public int FaultThreadId;
        public ulong DebugRsp;
        public ulong DebugRip;
        public ulong DebugStackStart;
        public uint DebugStackSize;
        public uint DebugStackRva;
        public int DebugThreads;
        public int DebugRanges;
        public readonly List<string> DebugThreadLines = new List<string>();

        private const uint Signature = 0x504D444D;

        private sealed class Range
        {
            public ulong Start;
            public ulong Size;
            public uint Rva;
        }

        private FileStream _stream;
        private BinaryReader _reader;
        public static string LastError = "";
        private uint _exceptionCtxSize;
        private uint _exceptionCtxRva;
        private readonly List<Range> _ranges = new List<Range>();

        public static MiniDump Load(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
            MiniDump dump = new MiniDump();
            try
            {
                if (!dump.Parse(path))
                {
                    if (LastError.Length == 0) LastError = "parse returned false";
                    return null;
                }
                return dump;
            }
            catch (Exception ex)
            {
                LastError = ex.ToString();
                return null;
            }
            finally
            {
                dump.Close();
            }
        }

        public string[] StackLines(int max)
        {
            int count = Math.Min(max, Frames.Count);
            string[] lines = new string[count];
            for (int i = 0; i < count; i++) lines[i] = Frames[i].Text;
            return lines;
        }

        private bool Parse(string path)
        {
            _stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            _reader = new BinaryReader(_stream);

            _stream.Position = 0;
            uint sig = _reader.ReadUInt32();
            if (sig != Signature)
            {
                LastError = "signature=0x" + sig.ToString("X8");
                return false;
            }
            uint version = _reader.ReadUInt32();
            uint streamCount = _reader.ReadUInt32();
            uint dirRva = _reader.ReadUInt32();
            _reader.ReadUInt32();
            _reader.ReadUInt32();
            _reader.ReadUInt64();

            uint threadRva = 0, threadSize = 0;
            uint moduleRva = 0, moduleSize = 0;
            uint memoryRva = 0, memorySize = 0;
            uint memory64Rva = 0, memory64Size = 0;
            uint exceptionRva = 0, exceptionSize = 0;

            _stream.Position = dirRva;
            for (uint i = 0; i < streamCount; i++)
            {
                uint type = _reader.ReadUInt32();
                uint size = _reader.ReadUInt32();
                uint rva = _reader.ReadUInt32();
                switch (type)
                {
                    case 3: threadRva = rva; threadSize = size; break;
                    case 4: moduleRva = rva; moduleSize = size; break;
                    case 5: memoryRva = rva; memorySize = size; break;
                    case 6: exceptionRva = rva; exceptionSize = size; break;
                    case 9: memory64Rva = rva; memory64Size = size; break;
                }
            }

            ReadModules(moduleRva);
            Modules.Sort(delegate(DumpModule a, DumpModule b) { return a.Base.CompareTo(b.Base); });
            ReadMemoryRanges(memoryRva, memory64Rva);
            if (exceptionRva != 0 && exceptionSize > 0) ReadException(exceptionRva);

            ThreadInfo fault = ReadThreads(threadRva, threadSize);
            if (fault == null) return Modules.Count > 0;
            DebugRsp = fault.Rsp;
            DebugRip = fault.Rip;
            DebugStackStart = fault.StackStart;
            DebugStackSize = fault.StackSize;
            DebugStackRva = fault.StackRva;
            ScanStack(fault);
            return true;
        }

        private sealed class ThreadInfo
        {
            public int Id;
            public ulong StackStart;
            public uint StackSize;
            public uint StackRva;
            public ulong ContextRva;
            public uint ContextSize;
            public ulong Rsp;
            public ulong Rip;
        }

        private void ReadModules(uint rva)
        {
            if (rva == 0) return;
            _stream.Position = rva;
            uint count = _reader.ReadUInt32();
            for (uint i = 0; i < count; i++)
            {
                long start = _stream.Position;
                DumpModule module = new DumpModule();
                module.Base = _reader.ReadUInt64();
                module.Size = _reader.ReadUInt32();
                _stream.Position = start + 20;
                uint nameRva = _reader.ReadUInt32();
                module.Name = ReadString(nameRva);
                if (module.Base != 0 && module.Size != 0) Modules.Add(module);
                _stream.Position = start + 108;
            }
        }

        private void ReadMemoryRanges(uint memoryRva, uint memory64Rva)
        {
            if (memoryRva != 0)
            {
                _stream.Position = memoryRva;
                uint count = _reader.ReadUInt32();
                for (uint i = 0; i < count; i++)
                {
                    Range range = new Range();
                    range.Start = _reader.ReadUInt64();
                    range.Size = _reader.ReadUInt32();
                    range.Rva = _reader.ReadUInt32();
                    if (range.Size > 0 && range.Rva != 0) _ranges.Add(range);
                }
            }

            if (memory64Rva != 0)
            {
                _stream.Position = memory64Rva;
                ulong count = _reader.ReadUInt64();
                ulong baseRva = _reader.ReadUInt64();
                ulong offset = 0;
                for (ulong i = 0; i < count; i++)
                {
                    Range range = new Range();
                    range.Start = _reader.ReadUInt64();
                    range.Size = _reader.ReadUInt64();
                    range.Rva = (uint)(baseRva + offset);
                    offset += range.Size;
                    if (range.Size > 0) _ranges.Add(range);
                }
            }
            _ranges.Sort(delegate(Range a, Range b) { return a.Start.CompareTo(b.Start); });
            DebugRanges = _ranges.Count;
        }

        private void ReadException(uint rva)
        {
            _stream.Position = rva;
            FaultThreadId = _reader.ReadInt32();
            _stream.Position = rva + 8;
            ExceptionCode = _reader.ReadUInt32();
            _reader.ReadUInt32();
            ExceptionAddress = _reader.ReadUInt64();
            _stream.Position = rva + 160;
            _exceptionCtxSize = _reader.ReadUInt32();
            _exceptionCtxRva = _reader.ReadUInt32();
        }

        private ThreadInfo ReadThreads(uint rva, uint size)
        {
            if (rva == 0) return null;
            _stream.Position = rva;
            uint count = _reader.ReadUInt32();
            List<ThreadInfo> threads = new List<ThreadInfo>();
            for (uint i = 0; i < count; i++)
            {
                long start = _stream.Position;
                ThreadInfo t = new ThreadInfo();
                t.Id = _reader.ReadInt32();
                _stream.Position = start + 24;
                t.StackStart = _reader.ReadUInt64();
                t.StackSize = _reader.ReadUInt32();
                t.StackRva = _reader.ReadUInt32();
                t.ContextSize = _reader.ReadUInt32();
                t.ContextRva = _reader.ReadUInt32();
                threads.Add(t);
                _stream.Position = start + 48;
            }
            DebugThreads = threads.Count;
            for (int i = 0; i < threads.Count; i++)
            {
                ThreadInfo t = threads[i];
                DebugThreadLines.Add("id=" + t.Id + " stack=0x" + t.StackStart.ToString("X")
                    + " size=" + t.StackSize + " rva=0x" + t.StackRva.ToString("X")
                    + " ctxSize=" + t.ContextSize + " ctxRva=0x" + t.ContextRva.ToString("X"));
            }

            ThreadInfo fault = null;
            for (int i = 0; i < threads.Count; i++)
            {
                if (threads[i].Id == FaultThreadId) { fault = threads[i]; break; }
            }
            if (fault == null && threads.Count > 0) fault = threads[0];
            if (fault == null) return null;

            if ((fault.ContextRva == 0 || fault.ContextSize < 256)
                && _exceptionCtxRva != 0 && _exceptionCtxSize >= 256)
            {
                fault.ContextRva = _exceptionCtxRva;
                fault.ContextSize = _exceptionCtxSize;
            }

            if (fault.ContextRva != 0 && fault.ContextSize >= 256)
            {
                _stream.Position = (long)fault.ContextRva;
                byte[] ctx = _reader.ReadBytes((int)fault.ContextSize);
                if (ctx.Length >= 256)
                {
                    fault.Rsp = BitConverter.ToUInt64(ctx, 152);
                    fault.Rip = BitConverter.ToUInt64(ctx, 248);
                }
            }
            return fault;
        }

        private void ScanStack(ThreadInfo thread)
        {
            ulong start = thread.StackStart;
            uint size = thread.StackSize;
            uint rva = thread.StackRva;
            if ((start == 0 || size == 0) && thread.Rsp != 0)
            {
                Range found = FindRange(thread.Rsp);
                if (found != null)
                {
                    start = found.Start;
                    size = (uint)Math.Min(found.Size, 256 * 1024);
                    rva = found.Rva;
                }
            }
            if (start == 0 || size == 0) return;
            if (thread.Rsp < start || thread.Rsp >= start + size) return;

            int limit = (int)Math.Min(size, 256 * 1024);
            byte[] stack = ReadBytes(start, limit);
            if (stack == null && rva != 0)
            {
                _stream.Position = rva;
                stack = _reader.ReadBytes(limit);
            }
            if (stack == null || stack.Length == 0) return;

            int offsetStart = (int)(thread.Rsp - start);
            offsetStart = (offsetStart + 7) & ~7;
            for (int offset = offsetStart; offset + 8 <= stack.Length && Frames.Count < 24; offset += 8)
            {
                ulong value = BitConverter.ToUInt64(stack, offset);
                DumpModule module = FindModule(value);
                if (module == null) continue;
                uint frameRva = (uint)(value - module.Base);
                if (!IsExecutable(module, frameRva)) continue;
                if (Frames.Count > 0 && Frames[Frames.Count - 1].Address == value) continue;
                DumpFrame frame = new DumpFrame();
                frame.Address = value;
                frame.Module = module.FileName;
                frame.ModuleBase = module.Base;
                frame.Rva = frameRva;
                Frames.Add(frame);
            }
        }

        private Range FindRange(ulong address)
        {
            int low = 0;
            int high = _ranges.Count - 1;
            while (low <= high)
            {
                int mid = (low + high) / 2;
                Range range = _ranges[mid];
                if (address < range.Start) high = mid - 1;
                else if (address >= range.Start + range.Size) low = mid + 1;
                else return range;
            }
            return null;
        }

        private DumpModule FindModule(ulong address)
        {
            int low = 0;
            int high = Modules.Count - 1;
            while (low <= high)
            {
                int mid = (low + high) / 2;
                DumpModule m = Modules[mid];
                if (address < m.Base) high = mid - 1;
                else if (address >= m.Base + m.Size) low = mid + 1;
                else return m;
            }
            return null;
        }

        private readonly Dictionary<string, List<ulong[]>> _execCache = new Dictionary<string, List<ulong[]>>();

        private bool IsExecutable(DumpModule module, uint rva)
        {
            List<ulong[]> ranges;
            if (!_execCache.TryGetValue(module.Name, out ranges))
            {
                ranges = PeSections.ReadExecutableRanges(module.Name);
                _execCache[module.Name] = ranges;
            }
            if (ranges == null || ranges.Count == 0) return true;
            for (int i = 0; i < ranges.Count; i++)
            {
                ulong[] range = ranges[i];
                if (rva >= range[0] && rva < range[1]) return true;
            }
            return false;
        }

        private byte[] ReadBytes(ulong address, int count)
        {
            for (int i = 0; i < _ranges.Count; i++)
            {
                Range range = _ranges[i];
                if (address < range.Start || address >= range.Start + range.Size) continue;
                int offset = (int)(address - range.Start);
                int available = (int)Math.Min((ulong)count, range.Size - (ulong)offset);
                if (available <= 0) return null;
                byte[] buffer = new byte[available];
                _stream.Position = range.Rva + offset;
                int read = _stream.Read(buffer, 0, available);
                if (read < available) Array.Resize(ref buffer, read);
                return buffer;
            }
            return null;
        }

        private string ReadString(uint rva)
        {
            if (rva == 0) return "";
            long back = _stream.Position;
            _stream.Position = rva;
            uint bytes = _reader.ReadUInt32();
            if (bytes == 0 || bytes > 4096) { _stream.Position = back; return ""; }
            byte[] data = _reader.ReadBytes((int)bytes);
            _stream.Position = back;
            return Encoding.Unicode.GetString(data);
        }

        private void Close()
        {
            try { if (_reader != null) _reader.Dispose(); } catch { }
            try { if (_stream != null) _stream.Dispose(); } catch { }
        }
    }

    internal static class PeSections
    {
        public static List<ulong[]> ReadExecutableRanges(string path)
        {
            List<ulong[]> ranges = new List<ulong[]>();
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
                using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (BinaryReader br = new BinaryReader(fs))
                {
                    if (fs.Length < 0x200) return null;
                    fs.Position = 0x3C;
                    int peOffset = br.ReadInt32();
                    if (peOffset <= 0 || peOffset + 24 > fs.Length) return null;
                    fs.Position = peOffset;
                    if (br.ReadUInt32() != 0x00004550) return null;
                    br.ReadUInt16();
                    ushort sections = br.ReadUInt16();
                    br.ReadUInt32();
                    br.ReadUInt16();
                    br.ReadUInt16();
                    ushort optionalSize = br.ReadUInt16();
                    br.ReadUInt16();

                    long optional = peOffset + 24;
                    fs.Position = optional;
                    ushort magic = br.ReadUInt16();
                    if (magic != 0x20B && magic != 0x10B) return null;

                    long sectionStart = optional + optionalSize;
                    for (int i = 0; i < sections; i++)
                    {
                        long pos = sectionStart + i * 40;
                        if (pos + 40 > fs.Length) break;
                        fs.Position = pos + 8;
                        uint virtualSize = br.ReadUInt32();
                        uint virtualAddress = br.ReadUInt32();
                        fs.Position = pos + 36;
                        uint characteristics = br.ReadUInt32();
                        if ((characteristics & 0x20000000) == 0) continue;
                        ulong begin = virtualAddress;
                        ulong end = (ulong)virtualAddress + Math.Max(virtualSize, 1u);
                        ranges.Add(new ulong[] { begin, end });
                    }
                }
            }
            catch { return null; }
            return ranges;
        }
    }
}
