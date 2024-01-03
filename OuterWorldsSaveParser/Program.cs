using Serilog;
using Serilog.Core;
using System.Globalization;
using System.IO.Compression;
using System.Reflection;
using System.Text;

namespace OuterWorldsSaveParser
{
    internal class Program
    {
        static Logger Logger { get; set; }

        static string logFilePath;

        static long lastCunkPosition;

        static void Main(string[] args)
        {
            Logger = CreateLogger();

            Logger.Debug($"Log file: {logFilePath}");

            var saveFilePaths = GetSaveFiles();

            foreach (var saveFilePath in saveFilePaths)
            {
                if (Path.GetFileName(saveFilePath) == "SaveGame.dat")
                {
                    //ProcessSaveFile(saveFilePath);
                    
                    // Since I haven't reverse engineered the save file format yet, I'm just going to dumb-parse it
                    DiscoverChunks(saveFilePath);
                    // Only process one save file for now
                    break;
                }
            }
        }

        static Logger CreateLogger()
        {
            var dateTime = DateTime.Now.ToString("yyyy-MM-dd-HH-mm-ss", CultureInfo.InvariantCulture);

            logFilePath = Path.GetFullPath($"{GetAssemblyName()}-{dateTime}.txt");

            return new LoggerConfiguration()
                .MinimumLevel.Debug()
                .WriteTo.Console()
                .WriteTo.File(logFilePath)
                .CreateLogger();
        }

        static string GetAssemblyName()
        {
            var assembly = Assembly.GetExecutingAssembly();
            var assemblyName = assembly.GetName();
            var programName = assemblyName.Name;

            return programName;
        }

        private static void DiscoverChunks(string saveFilePath)
        {
            var saveStream = new FileStream(saveFilePath, FileMode.Open, FileAccess.Read);
            var decompressed = new MemoryStream();

            DecompressSave(saveStream, decompressed);

            var workingDirectory = Path.Join(Path.GetTempPath(), GetAssemblyName(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(workingDirectory);

            File.WriteAllBytes(Path.Join(workingDirectory, Path.GetFileName(saveFilePath)), decompressed.ToArray());

            var chunkIndex = 0;
            var lastChunkLength = default(long);

            var reader = new BinaryReader(decompressed);
            reader.BaseStream.Seek(0, SeekOrigin.Begin);

            for (var i = 0; (reader.BaseStream.Position + 4) <= reader.BaseStream.Length; i++)
            {
                reader.BaseStream.Seek(i, SeekOrigin.Begin);

                var possibleChunkLength = reader.ReadInt32();

                if (possibleChunkLength == 5)
                {
                    var possibleChunkName = reader.ReadBytes(possibleChunkLength);
                     
                    if (IsChunkName(possibleChunkName))
                    {
                        lastChunkLength = i - lastCunkPosition;

                        var chunkName = Encoding.UTF8.GetString(possibleChunkName, 0, 4);

                        if (chunkIndex > 0)
                        {
                            WriteChunk(workingDirectory, decompressed.ToArray(), lastCunkPosition, lastChunkLength);
                        }
                        
                        lastCunkPosition = i;
                        chunkIndex++;
                    }

                    WriteChunk(workingDirectory, decompressed.ToArray(), lastCunkPosition, lastChunkLength);
                }
            }
        }

        private static void WriteChunk(string targetDirectory, byte[] saveData, long chunkPosition, long chunkLength)
        {
            if (chunkPosition < 0 || chunkPosition > saveData.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(chunkPosition));
            }

            var chunkName = new byte[5];
            Buffer.BlockCopy(saveData, (int) lastCunkPosition + 4, chunkName, 0, 5);
            var chunkNameString = Encoding.UTF8.GetString(chunkName, 0, 4);

            var chunkData = new byte[chunkLength];
            Buffer.BlockCopy(saveData, (int) lastCunkPosition, chunkData, 0, (int) chunkLength);

            var chunkFilePath = Path.Join(targetDirectory, $"{lastCunkPosition}-{chunkNameString}_0x{lastCunkPosition:X}.bin");
            File.WriteAllBytes(chunkFilePath, chunkData);

            Logger.Debug($"Wrote chunk: {chunkNameString}\tOffset: {chunkPosition}\t0x{chunkPosition:X}\tlength: {chunkLength}\t0x{chunkLength:X}");
        }

        private static bool IsChunkName(byte[] bytes)
        {
            for (var i = 0; i < bytes.Length; i++)
            {
                var b = bytes[i];

                if (!IsValidChunkChar(b, i))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsValidChunkChar(byte b, int index)
        {
            if (index == 0x04)
            {
                return (b == 0x00);
            }

            if (b >= 'A' && b <= 'Z')
            {
                return true;
            }

            return false;
        }

        private static void ProcessSaveFile(string saveFilePath)
        {
            var saveStream = new FileStream(saveFilePath, FileMode.Open, FileAccess.Read);
            var saveReader = new BinaryReader(saveStream);

            var decompressed = new MemoryStream();
            DecompressSave(saveStream, decompressed);

            decompressed.Seek(0, SeekOrigin.Begin);
            ParseSaveFile(decompressed);
        }

        private static void ParseSaveFile(MemoryStream decompressed)
        {
            var saveReader = new BinaryReader(decompressed);

            while(decompressed.Position < decompressed.Length)
            {
                var chunkHeaderLength = saveReader.ReadInt32();
                var chunkHeader = saveReader.ReadBytes(chunkHeaderLength);

                // parse outer wilds chunk


                var unk1 = saveReader.ReadInt32();
                var unk2 = saveReader.ReadInt16();

                var entryNameLength = saveReader.ReadInt32();
                var entryName = saveReader.ReadBytes(entryNameLength);

                var unk3 = saveReader.ReadInt32();

                Console.WriteLine($"Chunk: {System.Text.Encoding.UTF8.GetString(chunkHeader)}");
                Console.WriteLine($"Entry: {System.Text.Encoding.UTF8.GetString(entryName)}");
                Console.WriteLine($"Unk1: {unk1}");
                Console.WriteLine($"Unk2: {unk2}");
                Console.WriteLine($"Unk3: {unk3}");
            }
        }

        private static void DecompressSave(Stream input, Stream output)
        {
            using (var zlibStream = new ZLibStream(input, CompressionMode.Decompress))
            {
                zlibStream.CopyTo(output);
            }
        }

        static List<string> GetSaveFiles()
        {
            var result = new List<string>();

            var homePath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var savesGamesPath = Path.Combine(homePath, "Saved Games");
            var outerWorldsPath = Path.Combine(savesGamesPath, "The Outer Worlds");

            foreach (var entry in Directory.EnumerateFiles(outerWorldsPath, "*.dat", SearchOption.AllDirectories))
            {
                result.Add(entry);
            }

            return result;
        }
    }
}