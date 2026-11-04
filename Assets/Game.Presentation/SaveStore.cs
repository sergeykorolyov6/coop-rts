using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Game.Presentation
{
    // save-load.md: save files in Application.persistentDataPath/saves, newest first.
    public static class SaveStore
    {
        public const string Extension = ".coopsave";

        public static string Folder => Path.Combine(Application.persistentDataPath, "saves");

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

        public static List<Entry> List()
        {
            var result = new List<Entry>();
            if (!Directory.Exists(Folder))
                return result;
            foreach (string path in Directory.GetFiles(Folder, "*" + Extension))
                result.Add(new Entry(path, File.GetLastWriteTime(path)));
            result.Sort((a, b) => b.Written.CompareTo(a.Written));
            return result;
        }

        public static string Write(byte[] bytes)
        {
            Directory.CreateDirectory(Folder);
            string path = Path.Combine(Folder, $"save_{DateTime.Now:yyyyMMdd_HHmmss}{Extension}");
            File.WriteAllBytes(path, bytes);
            return path;
        }
    }
}
