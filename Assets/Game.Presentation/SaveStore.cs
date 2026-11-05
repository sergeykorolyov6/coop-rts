using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Game.Presentation
{
    // save-load.md: save files in Application.persistentDataPath/saves, replays in /replays, newest
    // first. Replays are written by themselves, so only the last MaxReplays are kept.
    public static class SaveStore
    {
        public enum Kind
        {
            Save,
            Replay,
        }

        private const int MaxReplays = 30;

        public sealed class Entry
        {
            public Entry(string path, DateTime written)
            {
                Path = path;
                Written = written;
            }

            public string Path { get; }
            public DateTime Written { get; }
        }

        public static string Folder(Kind kind) => Path.Combine(Application.persistentDataPath, kind == Kind.Save ? "saves" : "replays");

        private static string Extension(Kind kind) => kind == Kind.Save ? ".coopsave" : ".cooprep";

        public static List<Entry> List(Kind kind)
        {
            var result = new List<Entry>();
            string folder = Folder(kind);
            if (!Directory.Exists(folder))
                return result;
            foreach (string path in Directory.GetFiles(folder, "*" + Extension(kind)))
                result.Add(new Entry(path, File.GetLastWriteTime(path)));
            result.Sort((a, b) => b.Written.CompareTo(a.Written));
            return result;
        }

        public static string Write(Kind kind, byte[] bytes)
        {
            string folder = Folder(kind);
            Directory.CreateDirectory(folder);
            string name = kind == Kind.Save ? "save" : "replay";
            string path = Path.Combine(folder, $"{name}_{DateTime.Now:yyyyMMdd_HHmmss}{Extension(kind)}");
            File.WriteAllBytes(path, bytes);
            if (kind == Kind.Replay)
                Prune();
            return path;
        }

        // Only files of this store: a path from its own listing.
        public static void Delete(Entry entry) => File.Delete(entry.Path);

        private static void Prune()
        {
            List<Entry> replays = List(Kind.Replay);
            for (int i = MaxReplays; i < replays.Count; i++)
                File.Delete(replays[i].Path);
        }
    }
}
