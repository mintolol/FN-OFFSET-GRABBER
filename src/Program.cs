using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using CUE4Parse.FileProvider;
using CUE4Parse.FileProvider.Objects;
using CUE4Parse.UE4.IO.Objects;
using CUE4Parse.UE4.Versions;
using CUE4Parse.Utils;

internal static class Program
{
    private class AppConfig
    {
        public string? PaksDirectory { get; set; }
        public string? AssetPath { get; set; }
        public string? OutputDirectory { get; set; }
        public string? OodleDll { get; set; }
        public string? OodleCompressor { get; set; }
        public string? OodleLevel { get; set; }
        public bool? NoBackup { get; set; }
    }

    private static int Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Console.Title = "Fortnite Offset Grabber / Oodle Repacker - UE6 x64";

        ColoredWriteLine(" Fortnite Offset Grabber / Oodle Repacker", ConsoleColor.Cyan);
        ColoredWriteLine(" CUE4Parse / Unreal Engine 6 / x64", ConsoleColor.DarkCyan);
        Console.WriteLine();

        bool isDropMode = false;
        if (args.Length >= 1
            && !args[0].StartsWith("--", StringComparison.Ordinal)
            && args[0].EndsWith(".uasset", StringComparison.OrdinalIgnoreCase)
            && File.Exists(args[0].Trim('"', ' ')))
        {
            isDropMode = true;
            string droppedFile = args[0].Trim('"', ' ');
            args = [.. args, "--repack", $"--input={droppedFile}"];
            ColoredWriteLine($"[DROP] {droppedFile}", ConsoleColor.Cyan);
            Console.WriteLine();
        }
        AppConfig config = LoadConfig();
        string? paksDirectory = GetOption(args, "--paks") ?? config.PaksDirectory;
        string? assetPath = GetOption(args, "--asset") ?? config.AssetPath;
        string? outputDirectory = GetOption(args, "--output") ?? config.OutputDirectory;
        bool repack = HasFlag(args, "--repack");
        bool exportCompressed = HasFlag(args, "--export-compressed");
        bool verify = HasFlag(args, "--verify");
        string? inputPath = GetOption(args, "--input");
        string? oodleDll = GetOption(args, "--oodle-dll") ?? config.OodleDll;
        string? oodleCompressor = GetOption(args, "--oodle-compressor") ?? config.OodleCompressor;
        string? oodleLevel = GetOption(args, "--oodle-level") ?? config.OodleLevel;
        bool noBackup = HasFlag(args, "--no-backup") || (config.NoBackup ?? false);

        if (string.IsNullOrWhiteSpace(paksDirectory))
        {
            Console.Write("Paks folder (.utoc/.ucas) > ");
            paksDirectory = Console.ReadLine()?.Trim('"', ' ');
        }

        if (string.IsNullOrWhiteSpace(paksDirectory) || !Directory.Exists(paksDirectory))
        {
            Error("Paks folder could not be found.");
            WaitForKeyAndExit(1);
            return 1;
        }

        if (string.IsNullOrWhiteSpace(assetPath))
        {
            if (isDropMode)
            {
                ColoredWriteLine("[DROP] config.json の AssetPath が空です。", ConsoleColor.Yellow);
                ColoredWriteLine("[DROP] config.json に AssetPath を設定するか、", ConsoleColor.Yellow);
                ColoredWriteLine("[DROP] --asset=\"FortniteGame/Content/...\" を引数で指定してください。", ConsoleColor.Yellow);
                Console.WriteLine();
            }

            Console.Write("Asset Path > ");
            assetPath = Console.ReadLine()?.Trim();
        }

        if (string.IsNullOrWhiteSpace(outputDirectory))
            outputDirectory = Path.Combine(AppContext.BaseDirectory, "Extracted");

        if (string.IsNullOrWhiteSpace(assetPath))
        {
            Error("Asset Path is empty.");
            WaitForKeyAndExit(1);
            return 1;
        }

        assetPath = NormalizeInputPath(assetPath);

        Console.WriteLine();
        ColoredWriteLine($"[+] Engine : {EGame.GAME_UE6_0}", ConsoleColor.Green);
        ColoredWriteLine($"[+] Paks   : {paksDirectory}", ConsoleColor.Green);
        ColoredWriteLine($"[+] Asset  : {assetPath}", ConsoleColor.Green);
        Console.WriteLine();

        var sw = Stopwatch.StartNew();
        var version = new VersionContainer(EGame.GAME_UE6_0);

        try
        {
            ColoredWriteLine("[INFO] Loading Paks...", ConsoleColor.Yellow);

            var provider = new DefaultFileProvider(
                paksDirectory,
                SearchOption.TopDirectoryOnly,
                isCaseInsensitive: true,
                versions: version);

            provider.Initialize();
            provider.Mount();

            sw.Stop();

            ColoredWriteLine($"[OK] Mount Done ({sw.Elapsed.TotalSeconds:F2}s, {provider.Files.Count:N0} files)", ConsoleColor.Green);

            var matches = provider.Files.Values
                .OfType<FIoStoreEntry>()
                .Where(x => PathEquals(x.Path, assetPath))
                .ToList();

            if (matches.Count == 0)
            {
                ColoredWriteLine("[NOT FOUND] Asset was not found.", ConsoleColor.Red);
                ColoredWriteLine("[INFO] Please verify if you specified the entire Fortnite Paks folder.", ConsoleColor.Yellow);

                provider.UnloadAllVfs();
                provider.Dispose();
                
                WaitForKeyAndExit(2);
                return 2;
            }

            foreach (var entry in matches)
            {
                PrintResult(entry);

                if (exportCompressed)
                {
                    ExportCompressedAsset(
                        entry,
                        inputPath,
                        outputDirectory!,
                        oodleDll,
                        oodleCompressor,
                        oodleLevel);
                }
                else if (repack)
                {
                    RepackSingleAsset(
                        entry,
                        inputPath,
                        oodleDll,
                        oodleCompressor,
                        oodleLevel,
                        noBackup);
                }
                else
                {
                    SaveAssetFile(entry, outputDirectory!);
                    SaveRawCompressionBlock(entry, outputDirectory!);
                    SaveModReadyBlockSlot(entry, outputDirectory!);
                }
            }

            provider.UnloadAllVfs();
            provider.Dispose();

            if (repack && verify)
            {
                Console.WriteLine();
                VerifyRepackedAsset(
                    paksDirectory,
                    assetPath,
                    inputPath);
            }

            WaitForKeyAndExit(0);
            return 0;
        }
        catch (Exception ex)
        {
            sw.Stop();

            ColoredWriteLine($"[ERROR] {ex.Message}", ConsoleColor.Red);

            WaitForKeyAndExit(10);
            return 10;
        }
    }

    private static AppConfig LoadConfig()
    {
        string configPath = Path.Combine(AppContext.BaseDirectory, "config.json");
        if (!File.Exists(configPath))
        {
            var defaultConfig = new AppConfig
            {
                PaksDirectory = @"C:\Program Files\Epic Games\Fortnite\FortniteGame\Content\Paks",
                AssetPath = "",
                OutputDirectory = "",
                OodleDll = "",
                OodleCompressor = "Kraken",
                OodleLevel = "Normal",
                NoBackup = false
            };

            try
            {
                string jsonString = JsonSerializer.Serialize(defaultConfig, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(configPath, jsonString);
            }
            catch
            {
                // ignore
            }

            return defaultConfig;
        }

        try
        {
            string jsonString = File.ReadAllText(configPath);
            return JsonSerializer.Deserialize<AppConfig>(jsonString) ?? new AppConfig();
        }
        catch
        {
            return new AppConfig();
        }
    }

    private static void WaitForKeyAndExit(int exitCode)
    {
        Console.WriteLine();
        ColoredWriteLine("press any key to exit...", ConsoleColor.DarkGray);
        Console.ReadKey();
    }

    private static void VerifyRepackedAsset(
        string paksDirectory,
        string assetPath,
        string? inputPath)
    {
        ColoredWriteLine($"[VERIFY] {assetPath}", ConsoleColor.Cyan);

        if (string.IsNullOrWhiteSpace(inputPath))
        {
            throw new ArgumentException(
                "VERIFY requires --input=\"modified.uasset\".");
        }

        inputPath = Path.GetFullPath(
            inputPath.Trim().Trim('"'));

        if (!File.Exists(inputPath))
        {
            throw new FileNotFoundException(
                "Modified uasset for VERIFY could not be found.",
                inputPath);
        }

        byte[] expected = File.ReadAllBytes(inputPath);

        var version = new VersionContainer(EGame.GAME_UE6_0);

        using var provider = new DefaultFileProvider(
            paksDirectory,
            SearchOption.TopDirectoryOnly,
            isCaseInsensitive: true,
            versions: version);

        provider.Initialize();
        provider.Mount();

        ColoredWriteLine($"[OK] Remounted ({provider.Files.Count:N0} files)", ConsoleColor.Green);

        var matches = provider.Files.Values
            .OfType<FIoStoreEntry>()
            .Where(x => PathEquals(x.Path, assetPath))
            .ToList();

        if (matches.Count == 0)
        {
            throw new InvalidDataException(
                "Target uasset does not exist in CUE4Parse file list after remounting.");
        }

        Exception? lastError = null;

        foreach (var entry in matches)
        {
            try
            {
                byte[] actual = entry.Read();
                ColoredWriteLine($"[OK] Read ({actual.Length:N0} bytes)", ConsoleColor.Green);

                if (actual.Length != expected.Length)
                {
                    throw new InvalidDataException(
                        "Remounted uasset size does not match --input.\n" +
                        $"Expected: 0x{expected.Length:X} ({expected.Length:N0})\n" +
                        $"Actual  : 0x{actual.Length:X} ({actual.Length:N0})");
                }

                int mismatch = FindFirstMismatch(
                    expected,
                    actual);

                if (mismatch >= 0)
                {
                    throw new InvalidDataException(
                        "Remounted uasset content does not match --input.\n" +
                        $"First mismatch offset: 0x{mismatch:X}\n" +
                        $"Expected byte: 0x{expected[mismatch]:X2}\n" +
                        $"Actual byte  : 0x{actual[mismatch]:X2}");
                }

                ColoredWriteLine($"[OK] VERIFY SUCCESS ({actual.Length:N0} bytes)", ConsoleColor.Green);

                return;
            }
            catch (Exception ex)
            {
                lastError = ex;
            }
        }

        throw new InvalidDataException(
            "Target uasset VERIFY failed after remounting.",
            lastError);
    }

    private static int FindFirstMismatch(
        byte[] expected,
        byte[] actual)
    {
        int length = Math.Min(
            expected.Length,
            actual.Length);

        for (int i = 0; i < length; i++)
        {
            if (expected[i] != actual[i])
                return i;
        }

        return expected.Length == actual.Length
            ? -1
            : length;
    }

    private static void PrintResult(FIoStoreEntry entry)
    {
        var reader = entry.IoStoreReader;
        var header = reader.TocResource.Header;

        long logicalOffset = (long)entry.Offset;
        long size = (long)entry.Size;

        long blockSize = (long)header.CompressionBlockSize;
        long blockIndex = blockSize > 0 ? logicalOffset / blockSize : 0;
        long offsetInBlock = blockSize > 0 ? logicalOffset % blockSize : 0;

        FIoStoreTocCompressedBlockEntry block = default;

        bool hasBlock =
            blockIndex >= 0 &&
            blockIndex < reader.TocResource.CompressionBlocks.Length;

        if (hasBlock)
            block = reader.TocResource.CompressionBlocks[blockIndex];

        long physicalBlockOffset = hasBlock ? (long)block.Offset : -1;

        long physicalExactOffset =
            hasBlock && block.CompressionMethodIndex == 0
                ? physicalBlockOffset + offsetInBlock
                : -1;

        ulong partitionSize = (ulong)header.PartitionSize;

        int physicalPartition =
            partitionSize > 0 &&
            partitionSize != ulong.MaxValue
                ? (int)((ulong)Math.Max(0, physicalBlockOffset) / partitionSize)
                : 0;

        long physicalPartitionOffset =
            partitionSize > 0 &&
            partitionSize != ulong.MaxValue
                ? (long)((ulong)Math.Max(0, physicalBlockOffset) % partitionSize)
                : physicalBlockOffset;

        string utocPath = reader.Path;

        string basePath = Path.Combine(
            Path.GetDirectoryName(utocPath) ?? string.Empty,
            Path.GetFileNameWithoutExtension(utocPath));

        string ucasPath =
            physicalPartition == 0
                ? basePath + ".ucas"
                : basePath + $"_s{physicalPartition}.ucas";
        ColoredWriteLine($"[FOUND] {entry.Path}", ConsoleColor.Cyan);
        ColoredWriteLine($"[INFO] UTOC: {utocPath}", ConsoleColor.Yellow);
        ColoredWriteLine($"[INFO] UCAS: {ucasPath}", ConsoleColor.Yellow);
        ColoredWriteLine($"[INFO] Size={size:N0}, Block={blockIndex:N0}, Offset=0x{logicalOffset:X}", ConsoleColor.Yellow);

        if (hasBlock)
        {
            ColoredWriteLine($"[OFFSET] UCAS=0x{physicalPartitionOffset:X}, Offset=0x{physicalBlockOffset:X}, Method={block.CompressionMethodIndex}", ConsoleColor.Magenta);
            TryPrintCompressionMethods(entry, (int)blockIndex);

            if (physicalExactOffset >= 0)
            {
                ColoredWriteLine($"[OFFSET] HxD Exact=0x{physicalExactOffset:X}", ConsoleColor.Magenta);
            }
            else
            {
                ColoredWriteLine("[INFO] HxD Exact=N/A (compressed block)", ConsoleColor.Yellow);
            }
        }

        Console.WriteLine();
    }

    private static void TryPrintCompressionMethods(FIoStoreEntry entry, int blockIndex)
    {
        try
        {
            var reader = entry.IoStoreReader;
            var methods = reader.TocResource.CompressionMethods;

            ColoredWriteLine($"[INFO] Methods: {string.Join(", ", methods)}", ConsoleColor.Yellow);

            if (blockIndex < 0 || blockIndex >= reader.TocResource.CompressionBlocks.Length)
                return;

            int selected = reader.TocResource.CompressionBlocks[blockIndex].CompressionMethodIndex;

            if (selected >= 0 && selected < methods.Length)
            {
                ColoredWriteLine($"[INFO] Selected: {methods[selected]}", ConsoleColor.Yellow);
            }
        }
        catch
        {
            // Ignored depending on CUE4Parse version differences.
        }
    }

    private static void ExportCompressedAsset(
        FIoStoreEntry entry,
        string? inputPath,
        string outputDirectory,
        string? oodleDll,
        string? compressorName,
        string? levelName)
    {
        if (string.IsNullOrWhiteSpace(inputPath))
        {
            throw new ArgumentException(
                "--export-compressed requires --input=modified.uasset.");
        }

        inputPath = Path.GetFullPath(inputPath.Trim().Trim('"'));

        if (!File.Exists(inputPath))
        {
            throw new FileNotFoundException(
                "Modified uasset could not be found.",
                inputPath);
        }

        Directory.CreateDirectory(outputDirectory);

        ColoredWriteLine($"[EXPORT] {entry.Path}", ConsoleColor.Cyan);

        byte[] modifiedAsset = File.ReadAllBytes(inputPath);

        var reader = entry.IoStoreReader;
        var header = reader.TocResource.Header;
        long blockSize = (long)header.CompressionBlockSize;

        if (blockSize <= 0)
            throw new IOException("Compression Block Size could not be retrieved.");

        long logicalOffset = (long)entry.Offset;
        long blockIndex = logicalOffset / blockSize;
        long offsetInBlock = logicalOffset % blockSize;

        if (blockIndex < 0 ||
            blockIndex >= reader.TocResource.CompressionBlocks.Length)
        {
            throw new IOException(
                $"Compression Block {blockIndex} is out of UTOC range.");
        }

        var block = reader.TocResource.CompressionBlocks[blockIndex];

        int compressedSize = checked((int)block.CompressedSize);
        int uncompressedSize = checked((int)block.UncompressedSize);
        int method = block.CompressionMethodIndex;

        ColoredWriteLine($"[INFO] Block={blockIndex:N0}, Slot=0x{compressedSize:X}, Asset=0x{modifiedAsset.Length:X}", ConsoleColor.Yellow);

        if (offsetInBlock != 0)
        {
            throw new NotSupportedException(
                "Current export only supports cases where the asset starts at the beginning of the compression block.\n" +
                $"Offset in Block = 0x{offsetInBlock:X}");
        }

        if (modifiedAsset.Length != uncompressedSize)
        {
            throw new InvalidDataException(
                "Stopped because the uncompressed size of the modified uasset differs from the original.\n" +
                $"Expected = 0x{uncompressedSize:X} ({uncompressedSize:N0})\n" +
                $"Actual   = 0x{modifiedAsset.Length:X} ({modifiedAsset.Length:N0})");
        }

        string methodName = "Unknown";
        try
        {
            var methods = reader.TocResource.CompressionMethods;
            if (method >= 0 && method < methods.Length)
                methodName = methods[method].ToString();
        }
        catch
        {
            // Ignored.
        }

        ColoredWriteLine($"[INFO] Method={methodName}", ConsoleColor.Yellow);

        if (!methodName.Equals("Oodle", StringComparison.OrdinalIgnoreCase))
        {
            throw new NotSupportedException(
                $"Export stopped because the compression method of the target block is not Oodle. Method={methodName}");
        }

        OodleRepacker.Initialize(oodleDll);

        string selectedCompressorName =
            string.IsNullOrWhiteSpace(compressorName)
                ? "Kraken"
                : compressorName;

        var compressor =
            OodleRepacker.ParseCompressor(selectedCompressorName);

        string selectedLevelName =
            string.IsNullOrWhiteSpace(levelName)
                ? "Normal"
                : levelName;

        var level =
            OodleRepacker.ParseLevel(selectedLevelName);

        ColoredWriteLine($"[INFO] Oodle={compressor}, Level={level}", ConsoleColor.Yellow);
        ColoredWriteLine("[INFO] Compressing...", ConsoleColor.Yellow);

        byte[] newCompressed =
            OodleRepacker.Compress(
                modifiedAsset,
                compressor,
                level,
                compressedSize);

        ColoredWriteLine($"[OK] Compressed: 0x{newCompressed.Length:X}", ConsoleColor.Green);

        if (newCompressed.Length > compressedSize)
        {
            throw new InvalidDataException(
                "Export stopped because the compressed data does not fit into the original compression slot.\n" +
                $"New       = 0x{newCompressed.Length:X}\n" +
                $"Slot      = 0x{compressedSize:X}\n" +
                $"Overflow  = 0x{newCompressed.Length - compressedSize:X}");
        }

        int padding = checked(compressedSize - newCompressed.Length);

        byte[] slot = new byte[compressedSize];
        Buffer.BlockCopy(
            newCompressed,
            0,
            slot,
            0,
            newCompressed.Length);

        string inputName = Path.GetFileNameWithoutExtension(inputPath);
        string outputPath = Path.Combine(
            outputDirectory,
            inputName + ".compressed.uasset");

        File.WriteAllBytes(outputPath, slot);

        ColoredWriteLine("[INFO] Verifying compression result...", ConsoleColor.Yellow);

        byte[] roundTrip =
            OodleRepacker.Decompress(
                newCompressed,
                uncompressedSize);

        int mismatch = FindFirstMismatch(
            modifiedAsset,
            roundTrip);

        if (mismatch >= 0)
        {
            throw new InvalidDataException(
                "Data after Oodle compression and decompression does not match the modified uasset.\n" +
                $"First mismatch offset: 0x{mismatch:X}");
        }

        ColoredWriteLine($"[OK] EXPORT SUCCESS: {outputPath}", ConsoleColor.Green);
        ColoredWriteLine($"[INFO] Compressed=0x{newCompressed.Length:X}, Padding=0x{padding:X}", ConsoleColor.Yellow);
    }

    private static void RepackSingleAsset(
        FIoStoreEntry entry,
        string? inputPath,
        string? oodleDll,
        string? compressorName,
        string? levelName,
        bool noBackup)
    {
        if (string.IsNullOrWhiteSpace(inputPath))
        {
            throw new ArgumentException(
                "--repack requires --input=modified.uasset.");
        }

        if (!File.Exists(inputPath))
        {
            throw new FileNotFoundException(
                "Modified uasset could not be found.",
                inputPath);
        }

        ColoredWriteLine($"[REPACK] {entry.Path}", ConsoleColor.Cyan);

        byte[] modifiedAsset = File.ReadAllBytes(inputPath);

        var reader = entry.IoStoreReader;
        var header = reader.TocResource.Header;

        long blockSize = (long)header.CompressionBlockSize;

        if (blockSize <= 0)
            throw new IOException("Compression Block Size could not be retrieved.");

        long logicalOffset = (long)entry.Offset;
        long blockIndex = logicalOffset / blockSize;
        long offsetInBlock = logicalOffset % blockSize;

        if (blockIndex < 0 ||
            blockIndex >= reader.TocResource.CompressionBlocks.Length)
        {
            throw new IOException(
                $"Compression Block {blockIndex} is out of UTOC range.");
        }

        var block =
            reader.TocResource.CompressionBlocks[blockIndex];

        long physicalBlockOffset = (long)block.Offset;

        int compressedSize =
            checked((int)block.CompressedSize);

        int uncompressedSize =
            checked((int)block.UncompressedSize);

        int method =
            block.CompressionMethodIndex;

        ColoredWriteLine($"[INFO] Block={blockIndex:N0}, Slot=0x{compressedSize:X}, Asset=0x{modifiedAsset.Length:X}", ConsoleColor.Yellow);
        ColoredWriteLine($"Physical Block Offset   : 0x{physicalBlockOffset:X}", ConsoleColor.Magenta);

        if (offsetInBlock != 0)
        {
            throw new NotSupportedException(
                "Current safe in-place repack only supports cases where the asset starts at the beginning of the compression block.\n" +
                $"Offset in Block = 0x{offsetInBlock:X}");
        }

        if (modifiedAsset.Length != uncompressedSize)
        {
            throw new InvalidDataException(
                "Stopped because the uncompressed size of the modified uasset differs from the original.\n" +
                $"Expected = 0x{uncompressedSize:X} ({uncompressedSize:N0})\n" +
                $"Actual   = 0x{modifiedAsset.Length:X} ({modifiedAsset.Length:N0})");
        }

        ulong partitionSize = (ulong)header.PartitionSize;

        int partition =
            partitionSize > 0 &&
            partitionSize != ulong.MaxValue
                ? (int)((ulong)Math.Max(0, physicalBlockOffset) / partitionSize)
                : 0;

        long partitionOffset =
            partitionSize > 0 &&
            partitionSize != ulong.MaxValue
                ? (long)((ulong)Math.Max(0, physicalBlockOffset) % partitionSize)
                : physicalBlockOffset;

        string utocPath = reader.Path;

        string basePath = Path.Combine(
            Path.GetDirectoryName(utocPath) ?? string.Empty,
            Path.GetFileNameWithoutExtension(utocPath));

        string ucasPath =
            partition == 0
                ? basePath + ".ucas"
                : basePath + $"_s{partition}.ucas";

        if (!File.Exists(ucasPath))
        {
            throw new FileNotFoundException(
                "Corresponding UCAS could not be found.",
                ucasPath);
        }

        ColoredWriteLine("[INFO] Checking original block...", ConsoleColor.Yellow);

        byte[] originalCompressed =
            ReadBytesAt(
                ucasPath,
                partitionOffset,
                compressedSize);

        ColoredWriteLine($"[INFO] Original Block=0x{originalCompressed.Length:X}", ConsoleColor.Yellow);

        OodleRepacker.Initialize(oodleDll);

        ColoredWriteLine("[INFO] Verifying original block...", ConsoleColor.Yellow);

        byte[] originalDecompressed =
            OodleRepacker.Decompress(
                originalCompressed,
                uncompressedSize);

        Success("Original block verified");

        string selectedCompressorName =
            string.IsNullOrWhiteSpace(compressorName)
                ? "Kraken"
                : compressorName;

        OodleRepacker.OodleCompressor compressor =
            OodleRepacker.ParseCompressor(selectedCompressorName);

        string selectedLevelName =
            string.IsNullOrWhiteSpace(levelName)
                ? "Normal"
                : levelName;

        OodleRepacker.OodleCompressionLevel level =
            OodleRepacker.ParseLevel(selectedLevelName);

        ColoredWriteLine($"[INFO] Oodle={compressor}, Level={level}", ConsoleColor.Yellow);
        ColoredWriteLine("[INFO] Compressing...", ConsoleColor.Yellow);

        byte[] newCompressed =
            OodleRepacker.Compress(
                modifiedAsset,
                compressor,
                level,
                compressedSize);

        ColoredWriteLine($"[OK] Compressed: 0x{newCompressed.Length:X}", ConsoleColor.Green);

        ColoredWriteLine(
            $"[+] Original Slot Size: 0x{compressedSize:X} ({compressedSize:N0})", ConsoleColor.Green);

        ColoredWriteLine(
            $"[+] Padding            : 0x{compressedSize - newCompressed.Length:X}", ConsoleColor.Green);

        if (!noBackup)
        {
            string backupPath = ucasPath + ".backup";

            if (!File.Exists(backupPath))
            {
                File.Copy(ucasPath, backupPath, false);
                Success("Backup created");
            }
            else
            {
                ColoredWriteLine("[INFO] Backup already exists", ConsoleColor.Yellow);
            }
        }

        ColoredWriteLine("[INFO] Writing to UCAS...", ConsoleColor.Yellow);

        OodleRepacker.ReplaceBlock(
            ucasPath,
            partitionOffset,
            newCompressed,
            compressedSize);

        ColoredWriteLine($"[OK] REPACK SUCCESS: UCAS=0x{partitionOffset:X}, Size=0x{newCompressed.Length:X}, Padding=0x{compressedSize - newCompressed.Length:X}", ConsoleColor.Green);
    }

    private static byte[] ReadBytesAt(
        string path,
        long offset,
        int size)
    {
        if (offset < 0)
            throw new ArgumentOutOfRangeException(nameof(offset));

        if (size < 0)
            throw new ArgumentOutOfRangeException(nameof(size));

        long fileLength = new FileInfo(path).Length;

        if (offset > fileLength - size)
        {
            throw new IOException(
                $"Out of UCAS range.\n" +
                $"Offset=0x{offset:X}\n" +
                $"Size=0x{size:X}\n" +
                $"FileSize=0x{fileLength:X}");
        }

        byte[] data = new byte[size];

        using var fs = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite);

        fs.Position = offset;

        int total = 0;

        while (total < data.Length)
        {
            int read = fs.Read(
                data,
                total,
                data.Length - total);

            if (read <= 0)
                throw new EndOfStreamException(
                    "Could not read required number of bytes from UCAS.");

            total += read;
        }

        return data;
    }

    private static void SaveRawCompressionBlock(
        FIoStoreEntry entry,
        string outputDirectory)
    {
        try
        {
            var reader = entry.IoStoreReader;
            var header = reader.TocResource.Header;

            long blockSize =
                (long)header.CompressionBlockSize;

            if (blockSize <= 0)
                throw new IOException(
                    "Compression Block Size could not be retrieved.");

            long blockIndex =
                (long)entry.Offset / blockSize;

            if (blockIndex < 0 ||
                blockIndex >= reader.TocResource.CompressionBlocks.Length)
            {
                throw new IOException(
                    $"Compression Block {blockIndex} is out of UTOC range.");
            }

            var block =
                reader.TocResource.CompressionBlocks[blockIndex];

            long physicalBlockOffset =
                (long)block.Offset;

            ulong partitionSize =
                (ulong)header.PartitionSize;

            int physicalPartition =
                partitionSize > 0 &&
                partitionSize != ulong.MaxValue
                    ? (int)((ulong)Math.Max(
                        0,
                        physicalBlockOffset) / partitionSize)
                    : 0;

            long partitionOffset =
                partitionSize > 0 &&
                partitionSize != ulong.MaxValue
                    ? (long)((ulong)Math.Max(
                        0,
                        physicalBlockOffset) % partitionSize)
                    : physicalBlockOffset;

            string utocPath = reader.Path;

            string basePath = Path.Combine(
                Path.GetDirectoryName(utocPath) ?? string.Empty,
                Path.GetFileNameWithoutExtension(utocPath));

            string ucasPath =
                physicalPartition == 0
                    ? basePath + ".ucas"
                    : basePath + $"_s{physicalPartition}.ucas";

            if (!File.Exists(ucasPath))
                throw new FileNotFoundException(
                    "Corresponding UCAS could not be found.",
                    ucasPath);

            int compressedSize =
                checked((int)block.CompressedSize);

            if (compressedSize <= 0)
                throw new IOException(
                    "CompressedSize of the compression block is 0.");

            byte[] raw =
                ReadBytesAt(
                    ucasPath,
                    partitionOffset,
                    compressedSize);

            Directory.CreateDirectory(outputDirectory);

            string relativePath =
                entry.Path
                    .Replace('\\', '/')
                    .TrimStart('/');

            while (relativePath.StartsWith(
                "../../../",
                StringComparison.Ordinal))
            {
                relativePath = relativePath[9..];
            }

            relativePath =
                relativePath.Replace(
                    '/',
                    Path.DirectorySeparatorChar);

            string outputPath =
                Path.GetFullPath(
                    Path.Combine(
                        outputDirectory,
                        relativePath + ".block.bin"));

            string outputRoot =
                Path.GetFullPath(outputDirectory)
                    .TrimEnd(
                        Path.DirectorySeparatorChar,
                        Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;

            if (!outputPath.StartsWith(
                outputRoot,
                StringComparison.OrdinalIgnoreCase))
            {
                throw new IOException(
                    "Saving aborted because output path fell outside safe boundaries.");
            }

            Directory.CreateDirectory(
                Path.GetDirectoryName(outputPath)!);

            File.WriteAllBytes(outputPath, raw);

            string displayPath = Path.GetRelativePath(AppContext.BaseDirectory, outputPath);
            ColoredWriteLine($"[OK] RAW BLOCK: {displayPath} (Size=0x{raw.Length:X}, Offset=0x{partitionOffset:X})", ConsoleColor.Green);
        }
        catch (Exception ex)
        {
            ColoredWriteLine($"[WARN] RAW BLOCK SAVE FAILED: {ex.Message}", ConsoleColor.Yellow);
        }
    }

    private static void SaveModReadyBlockSlot(
        FIoStoreEntry entry,
        string outputDirectory)
    {
        try
        {
            var reader = entry.IoStoreReader;
            var header = reader.TocResource.Header;

            long blockSize =
                (long)header.CompressionBlockSize;

            if (blockSize <= 0)
                throw new IOException(
                    "Compression Block Size could not be retrieved.");

            long blockIndex =
                (long)entry.Offset / blockSize;

            if (blockIndex < 0 ||
                blockIndex >= reader.TocResource.CompressionBlocks.Length)
            {
                throw new IOException(
                    $"Compression Block {blockIndex} is out of UTOC range.");
            }

            var block =
                reader.TocResource.CompressionBlocks[blockIndex];

            long physicalBlockOffset =
                (long)block.Offset;

            long compressedSize =
                checked((long)block.CompressedSize);

            if (compressedSize <= 0)
                throw new IOException(
                    "CompressedSize of the compression block is 0.");

            ulong partitionSize =
                (ulong)header.PartitionSize;

            int partition =
                partitionSize > 0 &&
                partitionSize != ulong.MaxValue
                    ? (int)((ulong)Math.Max(
                        0,
                        physicalBlockOffset) / partitionSize)
                    : 0;

            long? nextBlockOffset = null;

            foreach (var candidate in
                     reader.TocResource.CompressionBlocks)
            {
                long candidateOffset =
                    (long)candidate.Offset;

                if (candidateOffset <= physicalBlockOffset)
                    continue;

                int candidatePartition =
                    partitionSize > 0 &&
                    partitionSize != ulong.MaxValue
                        ? (int)((ulong)candidateOffset / partitionSize)
                        : 0;

                if (candidatePartition != partition)
                    continue;

                if (!nextBlockOffset.HasValue ||
                    candidateOffset < nextBlockOffset.Value)
                {
                    nextBlockOffset = candidateOffset;
                }
            }

            string utocPath = reader.Path;

            string basePath = Path.Combine(
                Path.GetDirectoryName(utocPath) ?? string.Empty,
                Path.GetFileNameWithoutExtension(utocPath));

            string ucasPath =
                partition == 0
                    ? basePath + ".ucas"
                    : basePath + $"_s{partition}.ucas";

            if (!File.Exists(ucasPath))
                throw new FileNotFoundException(
                    "Corresponding UCAS could not be found.",
                    ucasPath);

            long fileLength =
                new FileInfo(ucasPath).Length;

            long slotSize =
                nextBlockOffset.HasValue
                    ? checked(nextBlockOffset.Value - physicalBlockOffset)
                    : compressedSize;

            if (slotSize < compressedSize)
            {
                throw new IOException(
                    $"Slot size to the next block (0x{slotSize:X}) is smaller than CompressedSize (0x{compressedSize:X}).");
            }

            byte[] rawSlot =
                ReadBytesAt(
                    ucasPath,
                    physicalBlockOffset,
                    checked((int)slotSize));

            int paddingLength =
                rawSlot.Length -
                checked((int)compressedSize);

            bool paddingAllZero = true;

            for (int i = checked((int)compressedSize);
                 i < rawSlot.Length;
                 i++)
            {
                if (rawSlot[i] != 0)
                {
                    paddingAllZero = false;
                    break;
                }
            }

            Directory.CreateDirectory(outputDirectory);

            string relativePath =
                entry.Path
                    .Replace('\\', '/')
                    .TrimStart('/');

            while (relativePath.StartsWith(
                "../../../",
                StringComparison.Ordinal))
            {
                relativePath = relativePath[9..];
            }

            relativePath =
                relativePath.Replace(
                    '/',
                    Path.DirectorySeparatorChar);

            string outputPath =
                Path.GetFullPath(
                    Path.Combine(
                        outputDirectory,
                        relativePath + ".modslot.bin"));

            string outputRoot =
                Path.GetFullPath(outputDirectory)
                    .TrimEnd(
                        Path.DirectorySeparatorChar,
                        Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;

            if (!outputPath.StartsWith(
                outputRoot,
                StringComparison.OrdinalIgnoreCase))
            {
                throw new IOException(
                    "Saving aborted because output path fell outside safe boundaries.");
            }

            Directory.CreateDirectory(
                Path.GetDirectoryName(outputPath)!);

            File.WriteAllBytes(
                outputPath,
                rawSlot);

            string displayPath = Path.GetRelativePath(AppContext.BaseDirectory, outputPath);
            ColoredWriteLine($"[OK] MOD SLOT: {displayPath} (Size=0x{rawSlot.Length:X}, Offset=0x{physicalBlockOffset:X}, ZeroPadding={paddingAllZero})", ConsoleColor.Green);
        }
        catch (Exception ex)
        {
            ColoredWriteLine($"[WARN] MOD SLOT SAVE FAILED: {ex.Message}", ConsoleColor.Yellow);
        }
    }

    private static void SaveAssetFile(
        FIoStoreEntry entry,
        string outputDirectory)
    {
        try
        {
            byte[] data = entry.Read();

            Directory.CreateDirectory(outputDirectory);

            string relativePath =
                entry.Path
                    .Replace('\\', '/')
                    .TrimStart('/');

            while (relativePath.StartsWith(
                "../../../",
                StringComparison.Ordinal))
            {
                relativePath = relativePath[9..];
            }

            relativePath =
                relativePath.Replace(
                    '/',
                    Path.DirectorySeparatorChar);

            string outputPath =
                Path.GetFullPath(
                    Path.Combine(
                        outputDirectory,
                        relativePath));

            string outputRoot =
                Path.GetFullPath(outputDirectory)
                    .TrimEnd(
                        Path.DirectorySeparatorChar,
                        Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;

            if (!outputPath.StartsWith(
                outputRoot,
                StringComparison.OrdinalIgnoreCase))
            {
                throw new IOException(
                    "Saving aborted because output path fell outside safe boundaries.");
            }

            Directory.CreateDirectory(
                Path.GetDirectoryName(outputPath)!);

            File.WriteAllBytes(
                outputPath,
                data);

            string displayPath = Path.GetRelativePath(AppContext.BaseDirectory, outputPath);
            ColoredWriteLine($"[OK] SAVED: {displayPath} ({data.Length:N0} bytes)", ConsoleColor.Green);
        }
        catch (Exception ex)
        {
            ColoredWriteLine($"[WARN] SAVE FAILED: {ex.Message}", ConsoleColor.Yellow);
        }
    }

    private static bool PathEquals(
        string a,
        string b)
    {
        a = NormalizeInputPath(a);
        b = NormalizeInputPath(b);

        if (string.Equals(
            a,
            b,
            StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (a.StartsWith(
            "../../../",
            StringComparison.Ordinal))
        {
            a = a[9..];
        }

        if (b.StartsWith(
            "../../../",
            StringComparison.Ordinal))
        {
            b = b[9..];
        }

        return string.Equals(
            a,
            b,
            StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeInputPath(
        string path)
    {
        path = path.Trim().Trim('"');
        path = path.Replace('\\', '/');

        while (path.StartsWith(
            "./",
            StringComparison.Ordinal))
        {
            path = path[2..];
        }

        while (path.StartsWith(
            "/",
            StringComparison.Ordinal))
        {
            path = path[1..];
        }

        return path;
    }

    private static string? GetOption(
        string[] args,
        string name)
    {
        foreach (string arg in args)
        {
            if (arg.StartsWith(
                name + "=",
                StringComparison.OrdinalIgnoreCase))
            {
                return arg[
                    (name.Length + 1)..]
                    .Trim('"');
            }
        }

        return null;
    }

    private static bool HasFlag(
        string[] args,
        string name)
    {
        return args.Any(x =>
            string.Equals(
                x,
                name,
                StringComparison.OrdinalIgnoreCase));
    }

    private static void ColoredWriteLine(string message, ConsoleColor color)
    {
        lock (Console.Out)
        {
            var oldColor = Console.ForegroundColor;
            Console.ForegroundColor = color;
            Console.WriteLine(message);
            Console.ForegroundColor = oldColor;
        }
    }

    private static void Error(string message)
    {
        ColoredWriteLine($"[ERROR] {message}", ConsoleColor.Red);
    }

    private static void Success(string message)
    {
        ColoredWriteLine($"[SUCCESS] {message}", ConsoleColor.Green);
    }
}

internal static class OodleRepacker
{
    public enum OodleCompressor : int
    {
        Kraken = 8,
        Mermaid = 9,
        Selkie = 11,
        Leviathan = 13
    }

    public enum OodleCompressionLevel : int
    {
        HyperFast4 = -4,
        HyperFast3 = -3,
        HyperFast2 = -2,
        HyperFast1 = -1,

        None = 0,
        SuperFast = 1,
        VeryFast = 2,
        Fast = 3,
        Normal = 4,

        Optimal1 = 5,
        Optimal2 = 6,
        Optimal3 = 7,
        Optimal4 = 8,
        Optimal5 = 9,
        Optimal6 = 10,
        Optimal7 = 11,
        Optimal8 = 12,
        Optimal9 = 13
    }

    private static readonly string[] OodleDllNames =
    {
        "oo2core_9_win64.dll",
        "oo2core_8_win64.dll",
        "oo2core_7_win64.dll",
        "oo2core_6_win64.dll",
        "oo2core_5_win64.dll",
        "oo2core_4_win64.dll",
        "oo2core_3_win64.dll",
        "oo2core.dll"
    };

    private static IntPtr _dll = IntPtr.Zero;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate long OodleLZ_CompressDelegate(
        OodleCompressor compressor,
        IntPtr rawBuf,
        long rawLen,
        IntPtr compBuf,
        OodleCompressionLevel level,
        IntPtr options,
        IntPtr dictionaryBase,
        IntPtr lrm,
        IntPtr scratchMem,
        long scratchSize);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate long OodleLZ_DecompressDelegate(
        IntPtr compBuf,
        long compBufSize,
        IntPtr rawBuf,
        long rawLen,
        int fuzzSafe,
        int checkCRC,
        int verbosity,
        IntPtr dictionaryBase,
        long dictionarySize,
        IntPtr callback,
        IntPtr callbackUserData,
        IntPtr scratchMem,
        long scratchSize,
        int threadPhase);

    private static OodleLZ_CompressDelegate? _compress;
    private static OodleLZ_DecompressDelegate? _decompress;

    public static void Initialize(
        string? explicitDll = null)
    {
        if (_dll != IntPtr.Zero)
            return;

        if (!string.IsNullOrWhiteSpace(explicitDll))
        {
            _dll = NativeLibrary.Load(explicitDll);
        }
        else
        {
            foreach (string name in OodleDllNames)
            {
                try
                {
                    _dll = NativeLibrary.Load(name);

                    if (_dll != IntPtr.Zero)
                        break;
                }
                catch
                {
                    // Try next DLL.
                }
            }
        }

        if (_dll == IntPtr.Zero)
        {
            throw new DllNotFoundException(
                "Oodle DLL could not be found.\n" +
                "Place oo2core_*_win64.dll in the same folder as the exe, or\n" +
                "specify --oodle-dll=\"C:\\path\\oo2core_*.dll\".");
        }

        IntPtr compressPtr =
            NativeLibrary.GetExport(
                _dll,
                "OodleLZ_Compress");

        IntPtr decompressPtr =
            NativeLibrary.GetExport(
                _dll,
                "OodleLZ_Decompress");

        _compress =
            Marshal.GetDelegateForFunctionPointer<OodleLZ_CompressDelegate>(
                compressPtr);

        _decompress =
            Marshal.GetDelegateForFunctionPointer<OodleLZ_DecompressDelegate>(
                decompressPtr);
    }

    public static byte[] Decompress(
        byte[] compressed,
        int uncompressedSize)
    {
        if (_decompress == null)
            throw new InvalidOperationException(
                "Oodle is not initialized.");

        byte[] output =
            new byte[uncompressedSize];

        GCHandle inputHandle =
            GCHandle.Alloc(
                compressed,
                GCHandleType.Pinned);

        GCHandle outputHandle =
            GCHandle.Alloc(
                output,
                GCHandleType.Pinned);

        try
        {
            long result =
                _decompress(
                    inputHandle.AddrOfPinnedObject(),
                    compressed.Length,
                    outputHandle.AddrOfPinnedObject(),
                    output.Length,
                    1,
                    0,
                    0,
                    IntPtr.Zero,
                    0,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    0,
                    3);

            if (result != uncompressedSize)
            {
                throw new InvalidDataException(
                    $"Oodle Decompress FAILED: result={result}, expected={uncompressedSize}");
            }

            return output;
        }
        finally
        {
            inputHandle.Free();
            outputHandle.Free();
        }
    }

    public static byte[] Compress(
        byte[] uncompressed,
        OodleCompressor compressor,
        OodleCompressionLevel level,
        int maxCompressedSize)
    {
        if (_compress == null)
            throw new InvalidOperationException(
                "Oodle is not initialized.");

        int capacity = Math.Max(
            uncompressed.Length * 2 + 4096,
            maxCompressedSize);

        byte[] compressed =
            new byte[capacity];

        GCHandle inputHandle =
            GCHandle.Alloc(
                uncompressed,
                GCHandleType.Pinned);

        GCHandle outputHandle =
            GCHandle.Alloc(
                compressed,
                GCHandleType.Pinned);

        try
        {
            long result =
                _compress(
                    compressor,
                    inputHandle.AddrOfPinnedObject(),
                    uncompressed.Length,
                    outputHandle.AddrOfPinnedObject(),
                    level,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    0);

            if (result <= 0)
            {
                throw new InvalidDataException(
                    $"Oodle Compress FAILED: result={result}");
            }

            if (result > maxCompressedSize)
            {
                throw new InvalidDataException(
                    "Compressed size exceeded the original compression slot.\n" +
                    $"Compressed = 0x{result:X} ({result:N0})\n" +
                    $"Slot       = 0x{maxCompressedSize:X} ({maxCompressedSize:N0})");
            }

            byte[] finalData =
                new byte[(int)result];

            Buffer.BlockCopy(
                compressed,
                0,
                finalData,
                0,
                (int)result);

            return finalData;
        }
        finally
        {
            inputHandle.Free();
            outputHandle.Free();
        }
    }

    public static void ReplaceBlock(
        string ucasPath,
        long offset,
        byte[] compressed,
        int slotSize)
    {
        if (compressed.Length > slotSize)
        {
            throw new InvalidOperationException(
                $"Compressed data does not fit into the slot.\n" +
                $"Compressed = 0x{compressed.Length:X}\n" +
                $"Slot       = 0x{slotSize:X}");
        }

        byte[] slot =
            new byte[slotSize];

        Buffer.BlockCopy(
            compressed,
            0,
            slot,
            0,
            compressed.Length);

        using var fs =
            new FileStream(
                ucasPath,
                FileMode.Open,
                FileAccess.ReadWrite,
                FileShare.Read);

        fs.Position = offset;

        fs.Write(
            slot,
            0,
            slot.Length);

        fs.Flush(true);
    }

    public static OodleCompressor ParseCompressor(
        string name)
    {
        return name.Trim().ToLowerInvariant() switch
        {
            "kraken" => OodleCompressor.Kraken,
            "mermaid" => OodleCompressor.Mermaid,
            "selkie" => OodleCompressor.Selkie,
            "leviathan" => OodleCompressor.Leviathan,

            _ => throw new ArgumentException(
                $"Unknown Oodle Compressor: {name}\n" +
                "Available: Kraken / Mermaid / Selkie / Leviathan")
        };
    }

    public static OodleCompressionLevel ParseLevel(
        string name)
    {
        if (Enum.TryParse<OodleCompressionLevel>(
            name,
            true,
            out var level))
        {
            return level;
        }

        if (int.TryParse(name, out int numeric) &&
            Enum.IsDefined(
                typeof(OodleCompressionLevel),
                numeric))
        {
            return (OodleCompressionLevel)numeric;
        }

        throw new ArgumentException(
            $"Unknown Oodle Compression Level: {name}");
    }
}
