using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DynamicData;

namespace GetStartedApp.Utils
{
    public enum PathType
    {
        File,
        Directory,
        Unknown
    }

    public static class FileUtils
    {

        /// <summary>
        /// Ritorna il tipo di elemento corrispondente al path
        /// </summary>
        /// <param name="path"></param>
        /// <returns></returns>
        public static PathType GetPathType(string path)
        {
            // Se il percorso esiste realmente
            if (File.Exists(path))
                return PathType.File;

            if (Directory.Exists(path))
                return PathType.Directory;

            return PathType.Unknown;
        }

        /// <summary>
        /// Popola una Lista con l'elenco di file/cartelle presenti nel path
        /// </summary>
        /// <param name="CurrentPath"></param>
        /// <returns></returns>
        public static List<FileSystemItem> PopolaTabellaFS(string CurrentPath, bool isDest, bool recursive = false)
        {
            List<FileSystemItem> fsList = new List<FileSystemItem>();

            if (Directory.Exists(CurrentPath))
            {
                try
                {
                    if (recursive) {
                        return GetAllFilesInPath(CurrentPath).OrderBy(f => f.FullPath).ToList();
                    }

                    // Cartelle
                    foreach (var dir in Directory.GetDirectories(CurrentPath))
                    {
                        var info = new DirectoryInfo(dir);
                        fsList.Add(new FileSystemItem
                        {
                            Name = info.Name,
                            IsDirectory = true,
                            Size = "",
                            LastModified = info.LastWriteTime,
                            FullPath = info.FullName
                        });
                    }

                    // File
                    if (!isDest)
                    {
                        foreach (var file in Directory.GetFiles(CurrentPath))
                        {
                            var info = new FileInfo(file);
                            fsList.Add(new FileSystemItem
                            {
                                Name = info.Name,
                                IsDirectory = false,
                                Size = $"{info.Length / 1024} KB",
                                LastModified = info.LastWriteTime,
                                FullPath = info.FullName
                            });
                        }
                    }
                }
                catch { /* ignorare errori di accesso */ }
            }

            return fsList;
        }

        public static List<FileSystemItem> GetAllFilesInPath(string path) { 
            List<FileSystemItem> fileSystemItems = new List<FileSystemItem>();

            // Ciclo tutte le cartelle
            foreach (var dir in Directory.GetDirectories(path))
            {
                fileSystemItems.Add(GetAllFilesInPath(dir));
            }

            foreach (var file in Directory.GetFiles(path))
            {
                var info = new FileInfo(file);
                fileSystemItems.Add(new FileSystemItem
                {
                    Name = info.Name,
                    IsDirectory = false,
                    Size = $"{info.Length / 1024} KB",
                    LastModified = info.LastWriteTime,
                    FullPath = info.FullName
                });
            }

            return fileSystemItems;
        }

        /// <summary>
        /// Calcola il CheckSum
        /// </summary>
        /// <param name="filePath"></param>
        /// <param name="algorithm"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentException"></exception>
        public static async Task<string> ComputeChecksumAsync(string path, string algorithm = "SHA256", CancellationToken ct = default)
        {
            using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            using HashAlgorithm hash = algorithm.ToUpperInvariant() switch
            {
                "MD5" => MD5.Create(),
                "SHA1" => SHA1.Create(),
                "SHA256" => SHA256.Create(),
                "SHA384" => SHA384.Create(),
                "SHA512" => SHA512.Create(),
                _ => throw new ArgumentException("Algoritmo non supportato", nameof(algorithm))
            };

            // lettura a blocchi (no ComputeHashAsync nativo)
            var buffer = new byte[1024 * 1024];
            int read;
            while ((read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), ct)) > 0)
            {
                hash.TransformBlock(buffer, 0, read, null, 0);
            }
            hash.TransformFinalBlock(Array.Empty<byte>(), 0, 0);

            StringBuilder sb = new StringBuilder(hash.Hash!.Length * 2);
            foreach (var b in hash.Hash)
            {
                sb.Append(b.ToString("x2"));
            }

            return sb.ToString();
        }

        /// <summary>
        /// Avvia il calcolo del checksum della lista di file prima della copia
        /// </summary>
        /// <param name="items"></param>
        /// <param name="algorithm"></param>
        /// <param name="maxDegreeOfParallelism"></param>
        /// <param name="progress"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        public static async Task PrecomputeChecksumsAsync(
            List<FileSystemItem> items,
            string algorithm = "SHA256",
            int maxDegreeOfParallelism = 4,
            IProgress<(int done, int total)>? progress = null,
            CancellationToken ct = default)
        {
            List<FileSystemItem> files = items.Where(i => !i.IsDirectory && File.Exists(i.FullPath)).ToList();
            int total = files.Count;
            int done = 0;

            using var sem = new SemaphoreSlim(maxDegreeOfParallelism);

            var tasks = files.Select(async item =>
            {
                await sem.WaitAsync(ct);
                try
                {
                    if (!string.IsNullOrEmpty(item.CheckSum))
                        return;

                    var cs = await ComputeChecksumAsync(item.FullPath, algorithm, ct);
                    item.CheckSum = cs;
                }
                finally
                {
                    sem.Release();
                    var d = Interlocked.Increment(ref done);
                    progress?.Report((d, total));
                }
            });

            await Task.WhenAll(tasks);
        }

        /// <summary>
        /// Passato un path ritorna il path superiore
        /// </summary>
        /// <param name="path"></param>
        /// <returns></returns>
        public static string? GoBackOneLevel(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return null;

            try
            {
                string? parent = Path.GetDirectoryName(path);

                // Se è null (es. siamo già in C:\ o in root), ritorna lo stesso path o null
                return string.IsNullOrEmpty(parent) ? path : parent;
            }
            catch
            {
                return path;
            }
        }

    }
}
