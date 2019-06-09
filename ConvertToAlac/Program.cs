using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace ConvertToAlac
{
    class Program
    {
        static IEnumerable<string> Files;
        static readonly ConcurrentQueue<List<string>> Directories = new ConcurrentQueue<List<string>>();

        static string OutputPath { get; set; }
        static string InputPath { get; set; } = ".";

        static void Main(string[] args)
        {
            if (args.Length < 2 || args.Length > 3)
            {
                ShowUsageAndExit();
            }

            for(int i = 0; i < args.Length; ++i)
            {
                if (args[i].StartsWith("-") && args[i].Length == 2)
                {
                    switch (args[i][1])
                    {
                        case 'o':
                            if (i + 1 == args.Length)
                            {
                                ShowUsageAndExit();
                            }
                            OutputPath = args[i + 1];
                            ++i;
                            break;
                    }
                }
                else
                {
                    InputPath = args[i];
                }
            }

            if (!Directory.Exists(InputPath))
            {
                Console.WriteLine($"Error: Path '{InputPath}' not found");
                ShowUsageAndExit();
            }

            Files = Directory.EnumerateFiles(InputPath, "*.flac", SearchOption.AllDirectories);

            if (Files.Count() == 0)
            {
                return;
            }
            
            var currentParent = Path.GetDirectoryName(Files.First());
            var currentList = new List<string>();
            foreach (var f in Files)
            {
                var parentDir = Path.GetDirectoryName(f);
                if (parentDir != currentParent)
                {
                    Directories.Enqueue(currentList);
                    currentParent = parentDir;
                    currentList = new List<string>();
                    currentList.Add(f);
                }
                else
                {
                    currentList.Add(f);
                }
            }

            if (currentList.Count > 0)
            {
                Directories.Enqueue(currentList);
            }

            var tasks = new List<Task>();
            for (int i = 0; i < Environment.ProcessorCount; ++i)
            {
                tasks.Add(Task.Run(() =>
                {
                    while (!Directories.IsEmpty)
                    {
                        ConvertDirectory();
                    }
                }));
            }

            Task.WaitAll(tasks.ToArray());
        }

        static void ShowUsageAndExit()
        {
            Console.WriteLine("Usage: ConvertToAlac [input path] -o <output path>");
            Console.WriteLine("If no input path is given, current directory is used.");
            Environment.Exit(-1);
        }

        static void ConvertDirectory()
        {
            if (Directories.TryDequeue(out List<string> dir))
            {
                foreach (var f in dir)
                {
                    var targetPath = Path.Combine(OutputPath, MakeRelative(f.Substring(0, f.Length - 5) + ".m4a"));
                    Directory.CreateDirectory(Path.GetDirectoryName(targetPath));
                    var process = new Process
                    {
                        StartInfo = new ProcessStartInfo
                        {
                            FileName = "ffmpeg",
                            Arguments = $"-loglevel error -y -i \"{f}\" -acodec alac \"{targetPath}\"",
                            RedirectStandardOutput = true,
                            RedirectStandardError = true,
                            CreateNoWindow = true,
                            UseShellExecute = false
                        }
                    };
                    Console.WriteLine(f);
                    process.Start();
                    // Ffmpeg outputs status on stderr to keep stdout open for people who want to pipe the actual files
                    process.StandardError.BaseStream.CopyTo(Console.OpenStandardOutput());
                    process.WaitForExit();
                }
            }
        }

        static string MakeRelative(string path)
        {
            return path.Substring(2);
        }
    }
}
