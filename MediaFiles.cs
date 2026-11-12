using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace blinkpip;

/// <summary>미디어·자막 파일 판별과 폴더 재생 목록 만들기</summary>
public static class MediaFiles
{
    public static readonly string[] VideoExt =
        { ".mp4", ".mkv", ".avi", ".mov", ".webm", ".wmv", ".flv", ".ts", ".m4v", ".mpg", ".mpeg", ".m2ts", ".3gp", ".ogv" };
    public static readonly string[] AudioExt = { ".mp3", ".flac", ".wav", ".m4a", ".ogg", ".opus", ".aac", ".wma" };
    public static readonly string[] SubtitleExt = { ".srt", ".ass", ".ssa", ".vtt", ".smi", ".sub" };

    private static readonly HashSet<string> MediaSet = new(VideoExt.Concat(AudioExt), StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> SubSet = new(SubtitleExt, StringComparer.OrdinalIgnoreCase);

    public static bool IsMedia(string path) => MediaSet.Contains(Path.GetExtension(path));
    public static bool IsSubtitle(string path) => SubSet.Contains(Path.GetExtension(path));

    /// <summary>파일 대화상자용 필터 문자열</summary>
    public static string OpenFilter =>
        "영상·음악|" + string.Join(";", VideoExt.Concat(AudioExt).Select(e => "*" + e)) + "|모든 파일|*.*";

    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
    private static extern int StrCmpLogicalW(string a, string b);

    /// <summary>탐색기와 같은 자연 정렬(file2 &lt; file10)</summary>
    public static void SortNatural(List<string> files) => files.Sort((a, b) => StrCmpLogicalW(Path.GetFileName(a), Path.GetFileName(b)));

    /// <summary>폴더 안의 미디어 파일을 자연 정렬해 돌려준다(하위 폴더 포함 선택 가능). 못 읽는 폴더는 건너뛰고, max 개까지만 담는다.</summary>
    public static List<string> FolderFiles(string dir, bool recursive, int max = 2000)
    {
        var opt = new EnumerationOptions { RecurseSubdirectories = recursive, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.System };
        var files = new List<string>();
        try
        {
            foreach (string f in Directory.EnumerateFiles(dir, "*", opt))
            {
                if (!IsMedia(f)) continue;
                files.Add(f);
                if (files.Count >= max) break;
            }
        }
        catch (Exception) { /* 읽은 데까지만 쓴다 */ }
        files.Sort(StrCmpLogicalW); // 전체 경로 기준이라 폴더 순서 → 파일 순서로 정렬된다
        return files;
    }

    /// <summary>m3u/m3u8 목록을 읽어 존재하는 로컬 경로와 URL 만 돌려준다(상대 경로는 목록 파일 위치 기준)</summary>
    public static List<string> ReadM3u(string path)
    {
        var result = new List<string>();
        string baseDir = Path.GetDirectoryName(Path.GetFullPath(path)) ?? "";
        foreach (string raw in File.ReadLines(path))
        {
            string line = raw.Trim().Trim('﻿');
            if (line.Length == 0 || line[0] == '#') continue;
            if (line.Contains("://")) { result.Add(line); continue; }
            string full = Path.IsPathRooted(line) ? line : Path.GetFullPath(Path.Combine(baseDir, line));
            if (File.Exists(full)) result.Add(full);
        }
        return result;
    }

    public static void WriteM3u(string path, IEnumerable<string> items) =>
        File.WriteAllLines(path, new[] { "#EXTM3U" }.Concat(items), new System.Text.UTF8Encoding(false));

    /// <summary>
    /// 파일이 있는 폴더의 미디어 파일을 자연 정렬해 재생 목록으로 만든다. index 는 목록에서 path 의 위치.
    /// 폴더를 못 읽거나 같은 폴더에 미디어가 path 하나뿐이면 null(그냥 단일 재생).
    /// </summary>
    public static (List<string> Files, int Index)? FolderPlaylist(string path, int max = 500)
    {
        try
        {
            string? dir = Path.GetDirectoryName(Path.GetFullPath(path));
            if (dir == null) return null;
            var files = Directory.EnumerateFiles(dir).Where(IsMedia).Take(max * 4).ToList();
            SortNatural(files);
            int idx = files.FindIndex(f => string.Equals(f, Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase));
            if (idx < 0 || files.Count < 2) return null;
            if (files.Count > max) // 너무 많으면 현재 파일 앞뒤로 자른다
            {
                int start = Math.Clamp(idx - max / 2, 0, files.Count - max);
                files = files.GetRange(start, max);
                idx -= start;
            }
            return (files, idx);
        }
        catch (Exception) { return null; } // 권한·경로 오류는 단일 재생으로 대체
    }
}
