using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using static BBDownT.Core.Entity.Entity;
using static BBDownT.BBDownTUtil;
using static BBDownT.Core.Util.SubUtil;
using static BBDownT.Core.Logger;
using System.IO;
using BBDownT.Core;
using System.Runtime.InteropServices;
using AudioMaterial = BBDownT.Core.Entity.Entity.AudioMaterial;

namespace BBDownT;

static partial class BBDownTMuxer
{
    public static string FFMPEG = "ffmpeg";
    public static string MP4BOX = "mp4box";

    // 非 --debug 时只要 error 级: 输出小且都是真问题, 便于失败时随调试信息一起补打;
    // --debug 下放宽到 verbose 以便看完整过程
    private static string[] FfmpegLogArgs() => ["-v", Config.DEBUG_LOG ? "verbose" : "error"];

    private static int RunExe(string app, IEnumerable<string> args, bool customBin = false)
    {
        int code = 0;
        Process p = new();
        p.StartInfo.FileName = app;
        foreach (var arg in args)
        {
            p.StartInfo.ArgumentList.Add(arg);
        }
        p.StartInfo.UseShellExecute = false;
        p.StartInfo.RedirectStandardError = true;
        p.StartInfo.CreateNoWindow = false;
        p.ErrorDataReceived += delegate (object sendProcess, DataReceivedEventArgs output) {
            if (!string.IsNullOrWhiteSpace(output.Data))
                LogDebug("{0}", output.Data);
        };
        p.StartInfo.StandardErrorEncoding = Encoding.UTF8;
        p.Start();
        p.BeginErrorReadLine();
        p.WaitForExit();
        code = p.ExitCode;
        p.Close();
        p.Dispose();
        return code;
    }

    private static string FormatCommandForLog(IEnumerable<string> args)
    {
        return string.Join(" ", args.Select(QuoteForLog));
    }

    private static string QuoteForLog(string arg)
    {
        if (arg.Length == 0)
        {
            return "\"\"";
        }

        return arg.Any(char.IsWhiteSpace) || arg.Contains('"')
            ? "\"" + arg.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\""
            : arg;
    }

    private static int MuxByMp4box(string videoPath, string audioPath, string outPath, string desc, string title, string author, string episodeId, string pic, string lang, List<Subtitle>? subs, bool audioOnly, bool videoOnly, List<ViewPoint>? points)
    {
        List<string> args = [];
        List<string> metaTags = [];
        int nowId = 0;
        if (Config.DEBUG_LOG) args.Add("-v");
        args.AddRange(["-inter", "500", "-noprog"]);
        if (!string.IsNullOrEmpty(videoPath))
        {
            args.AddRange(["-add", $"{videoPath}#trackID={(audioOnly && audioPath == "" ? "2" : "1")}:name="]);
            nowId++;
        }
        if (!string.IsNullOrEmpty(audioPath))
        {
            args.AddRange(["-add", $"{audioPath}:lang={(lang == "" ? "und" : lang)}"]);
            nowId++;
        }
        if (points != null && points.Any())
        {
            var meta = GetMp4boxMetaString(points);
            var metaFile = Path.Combine(Path.GetDirectoryName(string.IsNullOrEmpty(videoPath) ? audioPath : videoPath)!, "chapters");
            File.WriteAllText(metaFile, meta);
            args.AddRange(["-chap", metaFile]);
        }
        if (!string.IsNullOrEmpty(pic))
            metaTags.Add($"cover={pic}");
        if (!string.IsNullOrEmpty(episodeId))
        {
            metaTags.Add($"album={title}");
            metaTags.Add($"title={episodeId}");
        }
        else
        {
            metaTags.Add($"title={title}");
        }
        metaTags.Add($"sdesc={desc}");
        metaTags.Add($"comment={desc}");
        metaTags.Add($"artist={author}");

        if (subs != null)
        {
            for (int i = 0; i < subs.Count; i++)
            {
                if (File.Exists(subs[i].path) && File.ReadAllText(subs[i].path!) != "")
                {
                    nowId++;
                    args.AddRange(["-add", $"{subs[i].path}#trackID=1:name=:hdlr=sbtl:lang={GetSubtitleCode(subs[i].lan).Item1}"]);
                    args.AddRange(["-udta", $"{nowId}:type=name:str={GetSubtitleCode(subs[i].lan).Item2}"]);
                }
            }
        }

        //----分析完毕
        if (metaTags.Any())
        {
            args.AddRange(["-itags", "tool=:" + string.Join(':', metaTags)]);
        }
        args.AddRange(["-new", "--", outPath]);
        LogDebug("mp4box命令: {0}", FormatCommandForLog(args));
        return RunExe(MP4BOX, args, MP4BOX != "mp4box");
    }

    public static int MuxAV(bool useMp4box, string bvid, string videoPath, string audioPath, List<AudioMaterial> audioMaterial, string outPath, string desc = "", string title = "", string author = "", string episodeId = "", string pic = "", string lang = "", List<Subtitle>? subs = null, bool audioOnly = false, bool videoOnly = false, List<ViewPoint>? points = null, long pubTime = 0, bool simplyMux = false, bool isHevc = false)
    {
        if (audioOnly && audioPath != "")
            videoPath = "";
        if (videoOnly)
            audioPath = "";
        if (useMp4box)
        {
            return MuxByMp4box(videoPath, audioPath, outPath, desc, title, author, episodeId, pic, lang, subs, audioOnly, videoOnly, points);
        }

        if (outPath.Contains('/') && ! Directory.Exists(Path.GetDirectoryName(outPath)))
            Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
        //----分析并生成-i参数
        List<string> inputArgs = [];
        List<string> metaArgs = [];
        byte inputCount = 0;
        foreach (string path in new[] { videoPath, audioPath })
        {
            if (!string.IsNullOrEmpty(path))
            {
                inputCount++;
                inputArgs.AddRange(["-i", path]);
            }
        }

        if (audioMaterial.Any())
        {
            byte audioCount = 0;
            metaArgs.AddRange(["-metadata:s:a:0", "title=原音频"]);
            foreach (var audio in audioMaterial)
            {
                inputCount++;
                audioCount++;
                inputArgs.AddRange(["-i", audio.path]);
                if (!string.IsNullOrWhiteSpace(audio.title)) metaArgs.AddRange([$"-metadata:s:a:{audioCount}", $"title={audio.title}"]);
                if (!string.IsNullOrWhiteSpace(audio.personName)) metaArgs.AddRange([$"-metadata:s:a:{audioCount}", $"artist={audio.personName}"]);
            }
        }

        if (!string.IsNullOrEmpty(pic))
        {
            inputCount++;
            inputArgs.AddRange(["-i", pic]);
        }

        if (subs != null)
        {
            for (int i = 0; i < subs.Count; i++)
            {
                if(File.Exists(subs[i].path) && File.ReadAllText(subs[i].path!) != "")
                {
                    inputCount++;
                    inputArgs.AddRange(["-i", subs[i].path]);
                    metaArgs.AddRange([$"-metadata:s:s:{i}", $"title={GetSubtitleCode(subs[i].lan).Item2}", $"-metadata:s:s:{i}", $"language={GetSubtitleCode(subs[i].lan).Item1}"]);
                }
            }
        }

        if (!string.IsNullOrEmpty(pic))
            metaArgs.AddRange([$"-disposition:v:{(audioOnly ? "0" : "1")}", "attached_pic"]);
        if (points != null && points.Any())
        {
            var meta = GetFFmpegMetaString(points);
            var metaFile = Path.Combine(Path.GetDirectoryName(string.IsNullOrEmpty(videoPath) ? audioPath : videoPath)!, "chapters");
            File.WriteAllText(metaFile, meta);
            inputArgs.AddRange(["-i", metaFile, "-map_chapters", inputCount.ToString()]);
        }

        foreach (var i in Enumerable.Range(0, inputCount))
        {
            inputArgs.AddRange(["-map", i.ToString()]);
        }

        //----分析完毕
        List<string> args = [.. FfmpegLogArgs(), "-y"];
        args.AddRange(inputArgs);
        args.AddRange(metaArgs);
        args.AddRange(BuildContainerMetadataArgs(desc, title, author, episodeId, lang, pubTime, simplyMux));
        args.AddRange(["-c:v", "copy", "-c:a", "copy"]);
        if (audioOnly && audioPath == "") args.Add("-vn");
        if (subs != null) args.AddRange(["-c:s", "mov_text"]);
        // fix macOS hev1, see https://discussions.apple.com/thread/253081863?sortBy=rank
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX) && isHevc) args.AddRange(["-tag:v:0", "hvc1"]);
        args.AddRange(["-movflags", "faststart", "-strict", "unofficial", "-strict", "-2", "-f", "mp4", "--", outPath]);

        LogDebug("ffmpeg命令: {0}", FormatCommandForLog(args));
        return RunExe(FFMPEG, args, FFMPEG != "ffmpeg");
    }

    /// <summary>
    /// 构造容器级文本标签参数, 供首次混流与已存在文件的元数据刷新共用
    /// </summary>
    private static List<string> BuildContainerMetadataArgs(string desc, string title, string author, string episodeId, string lang, long pubTime, bool simplyMux)
    {
        // 复制章节标题的同时, 继续禁用输入的全局与流级元数据继承
        List<string> args = ["-map_metadata:g", "-1", "-map_metadata:s", "-1"];
        if (simplyMux) return args;
        args.AddRange(["-metadata", $"title={(episodeId == "" ? title : episodeId)}"]);
        args.AddRange(["-metadata", $"comment={desc}"]);
        if (lang != "") args.AddRange(["-metadata:s:a:0", $"language={lang}"]);
        args.AddRange(["-metadata", $"description={desc}"]);
        if (!string.IsNullOrEmpty(author)) args.AddRange(["-metadata", $"artist={author}"]);
        if (episodeId != "") args.AddRange(["-metadata", $"album={title}"]);
        if (pubTime != 0) args.AddRange(["-metadata", $"creation_time={DateTimeOffset.FromUnixTimeSeconds(pubTime).ToString("yyyy-MM-ddTHH:mm:ss.ffffffZ")}"]);
        return args;
    }

    /// <summary>
    /// 把原文件已有的容器/流级标签显式写回。刷新用 -map_metadata -1 关闭了继承,
    /// 而音频语言、字幕标题这些信息在接口只返回试看片段时拿不回来, 不补就会把原文件的标签清成 und。
    /// 参数排在最新解析出的标签之前, 同键由后者胜出。
    /// </summary>
    private static List<string> BuildPreservedMetadataArgs(MediaProbe.FileTags? preserved, bool hasPic,
        bool audioOnly, int newSubtitleCount, bool simplyMux)
    {
        var args = new List<string>();
        // --simply-mux 本来就不写描述/作者这类标签, 透传也保持关闭
        if (preserved is null || simplyMux) return args;

        foreach (var tag in preserved.FormatTags)
            args.AddRange(["-metadata", $"{tag.Key}={tag.Value}"]);

        foreach (var stream in preserved.Streams)
        {
            // 输出流序号与输入的对应关系由上面的 -map 决定, 这里必须跟着同一套规则
            string? selector = stream.CodecType switch
            {
                // 纯音频换封面时不映射任何输入视频流
                "video" when audioOnly && hasPic => null,
                // 换封面只留主视频流, 旧封面流连同它的标签一起丢
                "video" when hasPic && stream.IndexInType > 0 => null,
                "video" => "v",
                "audio" => "a",
                // 有新字幕时旧字幕流不再映射, 其标签也不该套到新字幕头上
                "subtitle" when newSubtitleCount == 0 => "s",
                _ => null,
            };
            if (selector is null) continue;
            foreach (var tag in stream.Tags)
            {
                // mp4 把字幕轨的标题读成 name, 但只认 title 这个写入键
                var key = tag.Key.Equals("name", StringComparison.OrdinalIgnoreCase) ? "title" : tag.Key;
                args.AddRange([$"-metadata:s:{selector}:{stream.IndexInType}", $"{key}={tag.Value}"]);
            }
        }
        return args;
    }

    /// <summary>
    /// 以已存在的输出文件为唯一媒体输入, 用 -c copy 重新封装并写回最新元数据;
    /// 音视频流只做搬运, 不重新下载也不重新编码。
    /// </summary>
    /// <param name="inputPath">已存在的输出文件</param>
    /// <param name="stagedPath">暂存输出文件, 成功后由调用方原子替换</param>
    public static int UpdateMetadata(string inputPath, string stagedPath, string desc = "", string title = "", string author = "",
        string episodeId = "", string pic = "", string lang = "", List<Subtitle>? subs = null, bool audioOnly = false,
        List<ViewPoint>? points = null, long pubTime = 0, bool simplyMux = false, MediaProbe.FileTags? preserved = null,
        Func<IEnumerable<string>, int>? runFfmpeg = null)
    {
        var usableSubs = subs?.Where(s => File.Exists(s.path) && File.ReadAllText(s.path) != "").ToList() ?? [];
        var hasPic = !string.IsNullOrEmpty(pic) && File.Exists(pic);
        byte inputCount = 1;                        // 0 号输入固定是已存在的媒体文件
        List<string> inputArgs = ["-i", inputPath]; // 输入选项组: 只允许 -i
        List<string> mapArgs = [];                  // 输出侧流映射: 必须整体排在最后一个 -i 之后
        List<string> metaArgs = [];
        byte picIndex = 0;
        var subIndices = new byte[usableSubs.Count];

        //----1. 先把所有 -i 连续拼完
        // ffmpeg 会把出现在某个 -i 之前的选项归入该输入文件的选项组; -map / -map_chapters 是输出选项,
        // 夹在 -i 之间会被当成输入选项, 直接报 "Option map ... cannot be applied to input url"。
        if (hasPic)
        {
            picIndex = inputCount++;
            inputArgs.AddRange(["-i", pic]);
        }

        for (int i = 0; i < usableSubs.Count; i++)
        {
            subIndices[i] = inputCount++;
            inputArgs.AddRange(["-i", usableSubs[i].path]);
        }

        string? chaptersFile = null;
        try
        {
            byte? chaptersIndex = null;
            // 接口没给章节时回读原文件里已有的, 否则刷新会把上次写进去的章节洗掉
            var effectivePoints = points is { Count: > 0 } ? points : preserved?.Chapters;
            if (effectivePoints is { Count: > 0 })
            {
                // 刷新路径没有临时音视频目录, 章节元数据写到系统临时目录并在结束后删除;
                // 写文件也放进 try: 写入中途失败时 finally 仍能清掉半截文件, 不留垃圾。
                chaptersFile = Path.Combine(Path.GetTempPath(), $"bbdownt-chapters-{Guid.NewGuid():N}");
                File.WriteAllText(chaptersFile, GetFFmpegMetaString(effectivePoints));
                chaptersIndex = inputCount++;
                inputArgs.AddRange(["-i", chaptersFile]);
            }

            //----2. 最后一个 -i 之后才是输出侧的流映射, 相对顺序与改动前保持一致
            // 只搬运已有媒体流: 提供新封面时丢掉旧的 attached_pic, 提供新字幕时丢掉旧字幕流,
            // 否则原样保留, 保证重复刷新幂等且不会因 --skip-cover/--skip-subtitle 丢内容。
            if (!(audioOnly && hasPic)) mapArgs.AddRange(["-map", hasPic ? "0:v:0?" : "0:v?"]);
            mapArgs.AddRange(["-map", "0:a?"]);
            if (usableSubs.Count == 0) mapArgs.AddRange(["-map", "0:s?"]);

            if (hasPic)
            {
                mapArgs.AddRange(["-map", picIndex.ToString()]);
                metaArgs.AddRange([$"-disposition:v:{(audioOnly ? "0" : "1")}", "attached_pic"]);
            }

            for (int i = 0; i < usableSubs.Count; i++)
            {
                mapArgs.AddRange(["-map", subIndices[i].ToString()]);
                metaArgs.AddRange([$"-metadata:s:s:{i}", $"title={GetSubtitleCode(usableSubs[i].lan).Item2}", $"-metadata:s:s:{i}", $"language={GetSubtitleCode(usableSubs[i].lan).Item1}"]);
            }

            if (chaptersIndex is not null) mapArgs.AddRange(["-map_chapters", chaptersIndex.Value.ToString()]);

            //----3. 拼接: -i 组 -> 映射组 -> 元数据组 -> 编码器/容器
            List<string> args = [.. FfmpegLogArgs(), "-y"];
            args.AddRange(inputArgs);
            args.AddRange(mapArgs);
            args.AddRange(metaArgs);
            args.AddRange(BuildPreservedMetadataArgs(preserved, hasPic, audioOnly, usableSubs.Count, simplyMux));
            args.AddRange(BuildContainerMetadataArgs(desc, title, author, episodeId, lang, pubTime, simplyMux));
            args.AddRange(["-c:v", "copy", "-c:a", "copy"]);
            args.AddRange(["-c:s", usableSubs.Count == 0 ? "copy" : "mov_text"]);
            args.AddRange(["-movflags", "faststart", "-strict", "unofficial", "-strict", "-2", "-f", "mp4", "--", stagedPath]);
            LogDebug("ffmpeg命令: {0}", FormatCommandForLog(args));
            return runFfmpeg is null ? RunExe(FFMPEG, args, FFMPEG != "ffmpeg") : runFfmpeg(args);
        }
        finally
        {
            if (chaptersFile != null && File.Exists(chaptersFile)) File.Delete(chaptersFile);
        }
    }

    public static void MergeFLV(string[] files, string outPath, Func<string, string, int>? convert = null)
    {
        if (files.Length == 0) throw new ArgumentException("没有可合并的分段", nameof(files));
        if (files.Length == 1)
        {
            CombineMultipleFilesIntoSingleFile(files, outPath);
            MediaOutput.DeleteInput(files[0], outPath);
        }
        else
        {
            convert ??= ConvertFlvSegment;
            var batch = Guid.NewGuid().ToString("N");
            var converted = files.Select(file => Path.Combine(Path.GetDirectoryName(file)!,
                Path.GetFileNameWithoutExtension(file) + "." + batch + ".ts")).ToArray();
            try
            {
                for (var i = 0; i < files.Length; i++)
                {
                    if (convert(files[i], converted[i]) != 0 || !File.Exists(converted[i])
                        || new FileInfo(converted[i]).Length == 0)
                        throw new IOException($"分段转换失败：{Path.GetFileName(files[i])}");
                }
                CombineMultipleFilesIntoSingleFile(converted, outPath);
                foreach (var file in files) MediaOutput.DeleteInput(file, outPath);
            }
            finally
            {
                foreach (var file in converted)
                    if (File.Exists(file)) File.Delete(file);
            }
        }
    }

    private static int ConvertFlvSegment(string input, string output)
    {
        List<string> args = [.. FfmpegLogArgs(), "-y", "-i", input, "-map", "0", "-c", "copy", "-f", "mpegts", "-bsf:v", "h264_mp4toannexb", output];
        LogDebug("ffmpeg命令: {0}", FormatCommandForLog(args));
        return RunExe(FFMPEG, args, FFMPEG != "ffmpeg");
    }
}
