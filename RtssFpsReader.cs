using System;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Text;

namespace SCPSLAlert
{
    /// <summary>RTSS 共享内存里读到的一份帧率。</summary>
    internal sealed class RtssFpsStats
    {
        public bool HasData;
        public double Fps;
        public double LowFps;
        public double FrameMs;
        public string Status = "";
    }

    /// <summary>
    /// 读 RivaTuner Statistics Server 的共享内存（RTSSSharedMemoryV2）。
    ///
    /// 这不是我们注入游戏：注入是 RTSS 自己做的（RTSSHooks64.dll Hook Present），
    /// 它把每个进程的帧率写进共享内存；MSI Afterburner / HWiNFO 等也是读这块内存。
    /// 所以前提是用户装了 RTSS 并让它保持运行。
    /// </summary>
    internal static class RtssFpsReader
    {
        private const uint Signature = 0x53535452; // "RTSS"
        private const string MapName = "RTSSSharedMemoryV2";
        private const int NameLength = 260;
        private const int MinEntrySize = 284;

        // RTSS_SHARED_MEMORY 头部（v2.x）：
        //   0  signature, 4 version, 8 appEntrySize, 12 appArrOffset, 16 appArrSize
        // RTSS_SHARED_MEMORY_APP_ENTRY（v2.0 起）：
        //   0 pid, 4 name[260], 264 flags, 268 time0, 272 time1,
        //   276 frames, 280 frameTime(us), 284 statFlags, 288 statTime0,
        //   292 statTime1, 296 statFrames, 300 statCount,
        //   304 statFpsMin, 308 statFpsAvg, 312 statFpsMax
        public static RtssFpsStats Read(int gamePid)
        {
            RtssFpsStats result = new RtssFpsStats();
            try
            {
                using (MemoryMappedFile mmf = MemoryMappedFile.OpenExisting(MapName, MemoryMappedFileRights.Read))
                using (MemoryMappedViewAccessor view = mmf.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read))
                {
                    if (view.Capacity < 36)
                    {
                        result.Status = "RTSS 共享内存太小";
                        return result;
                    }

                    if (view.ReadUInt32(0) != Signature)
                    {
                        result.Status = "RTSS 共享内存未初始化";
                        return result;
                    }

                    uint entrySize = view.ReadUInt32(8);
                    uint arrOffset = view.ReadUInt32(12);
                    uint arrSize = view.ReadUInt32(16);
                    if (entrySize < MinEntrySize || entrySize > 4096 || arrOffset < 36)
                    {
                        result.Status = "RTSS 共享内存格式不支持";
                        return result;
                    }

                    // dwAppArrSize 在 v2 头里是字节数；兼容把它当条目数的旧写法。
                    int count = (int)(arrSize / entrySize);
                    if (count <= 0 || count > 1024) count = (int)arrSize;
                    if (count <= 0 || count > 1024) count = 256;

                    long capacity = view.Capacity;
                    bool foundEntry = false;
                    uint now = unchecked((uint)Environment.TickCount);

                    for (int i = 0; i < count; i++)
                    {
                        long off = (long)arrOffset + (long)i * entrySize;
                        if (off + MinEntrySize > capacity) break;

                        int pid = view.ReadInt32(off);
                        string name = ReadName(view, off + 4);
                        if (gamePid > 0 ? pid != gamePid : !IsGameName(name)) continue;

                        foundEntry = true;
                        uint time0 = view.ReadUInt32(off + 268);
                        uint time1 = view.ReadUInt32(off + 272);
                        uint frames = view.ReadUInt32(off + 276);
                        uint frameTimeUs = view.ReadUInt32(off + 280);

                        // RTSS 的 time1 是 GetTickCount 毫秒；超过 3 秒没更新就是过期数据。
                        int age = unchecked((int)(now - time1));
                        if (age < 0 || age > 3000) continue;

                        double fps = 0;
                        if (time1 > time0 && frames > 0)
                            fps = 1000.0 * frames / (time1 - time0);
                        else if (frameTimeUs > 0)
                            fps = 1000000.0 / frameTimeUs;

                        if (fps < 0.1 || fps > 2000) continue;

                        uint statCount = view.ReadUInt32(off + 300);
                        uint statMin = view.ReadUInt32(off + 304);
                        result.HasData = true;
                        result.Fps = fps;
                        result.LowFps = statCount > 0 && statMin > 0 && statMin < 2000 ? statMin : fps;
                        result.FrameMs = frameTimeUs > 0 ? frameTimeUs / 1000.0 : 1000.0 / fps;
                        result.Status = "RTSS 共享内存";
                        return result;
                    }

                    result.Status = foundEntry ? "RTSS 数据已过期" : "RTSS 未捕获到游戏";
                    return result;
                }
            }
            catch (FileNotFoundException)
            {
                result.Status = "未检测到 RTSS";
            }
            catch (IOException)
            {
                result.Status = "未检测到 RTSS";
            }
            catch (Exception ex)
            {
                result.Status = "RTSS 读取失败：" + ex.Message;
            }
            return result;
        }

        private static string ReadName(MemoryMappedViewAccessor view, long offset)
        {
            byte[] buf = new byte[NameLength];
            view.ReadArray(offset, buf, 0, NameLength);
            int z = 0;
            while (z < buf.Length && buf[z] != 0) z++;
            return Encoding.Default.GetString(buf, 0, z);
        }

        private static bool IsGameName(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            return name.Equals("SCPSL.exe", StringComparison.OrdinalIgnoreCase)
                || name.EndsWith("\\SCPSL.exe", StringComparison.OrdinalIgnoreCase)
                || name.EndsWith("/SCPSL.exe", StringComparison.OrdinalIgnoreCase);
        }
    }
}
