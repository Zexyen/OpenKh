using OpenKh.Common;
using OpenKh.Kh1;
using OpenKh.Kh2;
using OpenKh.Recom;
using OpenKh.Tools.ModsManager.Interfaces;
using OpenKh.Tools.ModsManager.Models;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xe.IO;

namespace OpenKh.Tools.ModsManager.Services
{
    public class GameDataExtractionService : IGameDataExtractionOperations
    {
        private const int BufferSize = 65536;
        private const string REMASTERED_FILES_FOLDER_NAME = "remastered";

        public class BadConfigurationException : Exception
        {
            public BadConfigurationException(string message) : base(message)
            {

            }
        }

        public async Task ExtractKh1Ps2EditionAsync(
            string isoLocation,
            string gameDataLocation,
            Action<float> onProgress)
        {
            await ExtractKh1Ps2EditionAsync(isoLocation, gameDataLocation, onProgress, CancellationToken.None);
        }

        public async Task ExtractKh1Ps2EditionAsync(
            string isoLocation,
            string gameDataLocation,
            Action<float> onProgress,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fileBlocks = File.OpenRead(isoLocation).Using(stream =>
            {
                var bufferedStream = new BufferedStream(stream);
                var idxBlock = IsoUtility.GetFileOffset(bufferedStream, "KINGDOM.IDX;1");
                var firstBlock = IsoUtility.GetFileOffset(bufferedStream, "SYSTEM.CNF;1");
                return (idxBlock, firstBlock);
            });

            if (fileBlocks.idxBlock == -1 || fileBlocks.firstBlock == -1)
            {
                throw new BadConfigurationException(
                    $"Unable to find the files KINGDOM.IDX and SYSTEM.CNF in the ISO at '{isoLocation}'. The extraction will stop."
                );
            }

            onProgress(0);

            await Task.Run(() =>
            {
                using var isoStream = File.OpenRead(isoLocation);

                var idxOffset = fileBlocks.idxBlock * 0x800L;
                var idxEntries = Idx1.Read(new SubStream(isoStream, idxOffset, isoStream.Length - idxOffset));

                var firstOffset = fileBlocks.firstBlock * 0x800L;
                var imgStream = new SubStream(isoStream, firstOffset, isoStream.Length - firstOffset);
                var img = new Img1(imgStream, idxEntries, 0);

                var fileCount = img.Entries.Count;
                var fileProcessed = 0;
                foreach (var fileEntry in img.Entries)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var fileName = Idx1Name.Lookup(fileEntry.Value) ?? $"@noname/{fileEntry.Value.Hash:X08}";
                    using var stream = img.FileOpen(fileEntry.Value);
                    if (stream == null)
                    {
                        Log.Warn($"Unable to extract {fileName}");
                        continue;
                    }
                    var fileDestination = Path.Combine(gameDataLocation, "kh1", fileName);
                    var directoryDestination = Path.GetDirectoryName(fileDestination);
                    if (!Directory.Exists(directoryDestination))
                    {
                        Directory.CreateDirectory(directoryDestination);
                    }
                    File.Create(fileDestination).Using(dstStream => stream.CopyTo(dstStream, BufferSize));

                    fileProcessed++;
                    onProgress((float)fileProcessed / fileCount);
                }

                onProgress(1.0f);
            }, cancellationToken);
        }

        public async Task ExtractRecomPs2EditionAsync(
            string isoLocation,
            string gameDataLocation,
            Action<float> onProgress)
        {
            await ExtractRecomPs2EditionAsync(isoLocation, gameDataLocation, onProgress, CancellationToken.None);
        }

        public async Task ExtractRecomPs2EditionAsync(
            string isoLocation,
            string gameDataLocation,
            Action<float> onProgress,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var stream = File.OpenRead(isoLocation);
            var rdi_stream = IsoUtility.GetSectors(stream, 0x244, stream.SetPosition(0x244 * 0x800 + 8).ReadInt16() + 1);
            var rdi = RootDirInfo.Read(rdi_stream);
            await Task.Run(() => {
                cancellationToken.ThrowIfCancellationRequested();
                rdi.ExtractFiles(stream, Path.Combine(gameDataLocation, "Recom"), onProgress);
            }, cancellationToken);
            stream.Close();
        }

        public async Task ExtractKh2Ps2EditionAsync(
            string isoLocation,
            string gameDataLocation,
            Action<float> onProgress)
        {
            await ExtractKh2Ps2EditionAsync(isoLocation, gameDataLocation, onProgress, CancellationToken.None);
        }

        public async Task ExtractKh2Ps2EditionAsync(
            string isoLocation,
            string gameDataLocation,
            Action<float> onProgress,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fileBlocks = File.OpenRead(isoLocation).Using(stream =>
            {
                var bufferedStream = new BufferedStream(stream);
                var idxBlock = IsoUtility.GetFileOffset(bufferedStream, "KH2.IDX;1");
                var imgBlock = IsoUtility.GetFileOffset(bufferedStream, "KH2.IMG;1");
                return (idxBlock, imgBlock);
            });

            if (fileBlocks.idxBlock == -1 || fileBlocks.imgBlock == -1)
            {
                throw new BadConfigurationException(
                    $"Unable to find the files KH2.IDX and KH2.IMG in the ISO at '{isoLocation}'. The extraction will stop."
                );
            }

            onProgress(0);

            await Task.Run(() =>
            {
                using var isoStream = File.OpenRead(isoLocation);

                var idxOffset = fileBlocks.idxBlock * 0x800L;
                var idx = Idx.Read(new SubStream(isoStream, idxOffset, isoStream.Length - idxOffset));

                var imgOffset = fileBlocks.imgBlock * 0x800L;
                var imgStream = new SubStream(isoStream, imgOffset, isoStream.Length - imgOffset);
                var img = new Img(imgStream, idx, true);

                var fileCount = img.Entries.Count;
                var fileProcessed = 0;
                foreach (var fileEntry in img.Entries)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var fileName = IdxName.Lookup(fileEntry) ?? $"@{fileEntry.Hash32:08X}_{fileEntry.Hash16:04X}";
                    using var stream = img.FileOpen(fileEntry);
                    var fileDestination = Path.Combine(gameDataLocation, "kh2", fileName);
                    var directoryDestination = Path.GetDirectoryName(fileDestination);
                    if (!Directory.Exists(directoryDestination))
                    {
                        Directory.CreateDirectory(directoryDestination);
                    }
                    File.Create(fileDestination).Using(dstStream => stream.CopyTo(dstStream, BufferSize));

                    fileProcessed++;
                    onProgress((float)fileProcessed / fileCount);
                }

                onProgress(1.0f);
            }, cancellationToken);
        }

        public async Task ExtractKhPcEditionAsync(
            string gameDataLocation,
            Action<float> onProgress,
            Func<string, string> getKhFilePath,
            Func<string, string> getKh3dFilePath,
            bool extractkh1,
            bool extractkh2,
            bool extractbbs,
            bool extractrecom,
            bool extractkh3d,
            Func<Exception, Task<bool>> ifRetry,
            CancellationToken cancellationToken)
        {
            await Task.Run(async () =>
            {
                var _nameListkh1 = new string[]
                {
                    "first",
                    "second",
                    "third",
                    "fourth",
                    "fifth"
                };
                var _nameListkh2 = new string[]
                {
                    "first",
                    "second",
                    "third",
                    "fourth",
                    "fifth",
                    "sixth"
                };
                var _nameListbbs = new string[]
                {
                    "first",
                    "second",
                    "third",
                    "fourth"
                };
                var _nameListkh3d = new string[]
                {
                    "first",
                    "second",
                    "third",
                    "fourth"
                };

                var _totalFiles = 0;
                var _procTotalFiles = 0;

                onProgress(0);

                if (extractkh1)
                {
                    for (int i = 0; i < 5; i++)
                    {
                        using var _stream = new FileStream(getKhFilePath("kh1_" + _nameListkh1[i] + ".hed"), System.IO.FileMode.Open);
                        var _hedFile = OpenKh.Egs.Hed.Read(_stream);
                        _totalFiles += _hedFile.Count();
                    }
                }
                if (extractkh2)
                {
                    for (int i = 0; i < 6; i++)
                    {
                        using var _stream = new FileStream(getKhFilePath("kh2_" + _nameListkh2[i] + ".hed"), System.IO.FileMode.Open);
                        var _hedFile = OpenKh.Egs.Hed.Read(_stream);
                        _totalFiles += _hedFile.Count();
                    }
                }
                if (extractbbs)
                {
                    for (int i = 0; i < 4; i++)
                    {
                        using var _stream = new FileStream(getKhFilePath("bbs_" + _nameListbbs[i] + ".hed"), System.IO.FileMode.Open);
                        var _hedFile = OpenKh.Egs.Hed.Read(_stream);
                        _totalFiles += _hedFile.Count();
                    }
                }
                if (extractrecom)
                {
                    using var _stream = new FileStream(getKhFilePath("Recom.hed"), System.IO.FileMode.Open);
                    var _hedFile = OpenKh.Egs.Hed.Read(_stream);
                    _totalFiles += _hedFile.Count();
                }
                if (extractkh3d)
                {
                    for (int i = 0; i < 4; i++)
                    {
                        using var _stream = new FileStream(getKh3dFilePath("kh3d_" + _nameListbbs[i] + ".hed"), System.IO.FileMode.Open);
                        var _hedFile = OpenKh.Egs.Hed.Read(_stream);
                        _totalFiles += _hedFile.Count();
                    }
                }

                async Task ProcessHedStreamAsync(string outputDir, Stream hedStream, Stream img)
                {
                    await Task.Yield();

                    foreach (var entry in OpenKh.Egs.Hed.Read(hedStream))
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                    retry:
                        try
                        {
                            var hash = OpenKh.Egs.Helpers.ToString(entry.MD5);
                            if (!OpenKh.Egs.EgsTools.Names.TryGetValue(hash, out var fileName))
                                fileName = $"{hash}.dat";

                            var outputFileName = Path.Combine(outputDir, fileName);

                            OpenKh.Egs.EgsTools.CreateDirectoryForFile(outputFileName);

                            var hdAsset = new OpenKh.Egs.EgsHdAsset(img.SetPosition(entry.Offset));

                            File.Create(outputFileName).Using(stream => stream.Write(hdAsset.OriginalData));

                            outputFileName = Path.Combine(outputDir, REMASTERED_FILES_FOLDER_NAME, fileName);

                            if (!ConfigurationService.SkipRemastered)
                            {

                                foreach (var asset in hdAsset.Assets)
                                {
                                    var outputFileNameRemastered = Path.Combine(OpenKh.Egs.EgsTools.GetHDAssetFolder(outputFileName), asset);

                                    OpenKh.Egs.EgsTools.CreateDirectoryForFile(outputFileNameRemastered);

                                    var assetData = hdAsset.RemasteredAssetsDecompressedData[asset];
                                    File.Create(outputFileNameRemastered).Using(stream => stream.Write(assetData));
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            if (await ifRetry(ex))
                            {
                                goto retry;
                            }
                        }

                        _procTotalFiles++;

                        onProgress((float)_procTotalFiles / _totalFiles);
                    }
                }

                if (extractkh1)
                {
                    for (int i = 0; i < 5; i++)
                    {
                        var outputDir = Path.Combine(gameDataLocation, "kh1");
                        using var hedStream = File.OpenRead(getKhFilePath("kh1_" + _nameListkh1[i] + ".hed"));
                        using var img = File.OpenRead(getKhFilePath("kh1_" + _nameListkh1[i] + ".pkg"));

                        await ProcessHedStreamAsync(outputDir, hedStream, img);
                    }
                }
                if (extractkh2)
                {
                    for (int i = 0; i < 6; i++)
                    {
                        var outputDir = Path.Combine(gameDataLocation, "kh2");
                        using var hedStream = File.OpenRead(getKhFilePath("kh2_" + _nameListkh2[i] + ".hed"));
                        using var img = File.OpenRead(getKhFilePath("kh2_" + _nameListkh2[i] + ".pkg"));

                        await ProcessHedStreamAsync(outputDir, hedStream, img);
                    }
                }
                if (extractbbs)
                {
                    for (int i = 0; i < 4; i++)
                    {
                        var outputDir = Path.Combine(gameDataLocation, "bbs");
                        using var hedStream = File.OpenRead(getKhFilePath("bbs_" + _nameListbbs[i] + ".hed"));
                        using var img = File.OpenRead(getKhFilePath("bbs_" + _nameListbbs[i] + ".pkg"));

                        await ProcessHedStreamAsync(outputDir, hedStream, img);
                    }
                }
                if (extractrecom)
                {
                    for (int i = 0; i < 1; i++)
                    {
                        var outputDir = Path.Combine(gameDataLocation, "Recom");
                        using var hedStream = File.OpenRead(getKhFilePath("Recom.hed"));
                        using var img = File.OpenRead(getKhFilePath("Recom.pkg"));

                        await ProcessHedStreamAsync(outputDir, hedStream, img);
                    }
                }
                if (extractkh3d)
                {
                    for (int i = 0; i < 4; i++)
                    {
                        var outputDir = Path.Combine(gameDataLocation, "kh3d");
                        using var hedStream = File.OpenRead(getKh3dFilePath("kh3d_" + _nameListkh3d[i] + ".hed"));
                        using var img = File.OpenRead(getKh3dFilePath("kh3d_" + _nameListkh3d[i] + ".pkg"));

                        await ProcessHedStreamAsync(outputDir, hedStream, img);
                    }
                }
                onProgress(1);
            }, cancellationToken);
        }

        public async Task<GameDataExtractionResult> ExtractAsync(
            GameDataExtractionRequest request,
            IProgress<GameDataExtractionProgress> progress = null,
            CancellationToken cancellationToken = default)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.DestinationPath))
                return new GameDataExtractionResult(OperationOutcome.Failure(
                    OperationFailureKind.InvalidRequest, "An extraction destination is required."));

            Action<float> report = value => progress?.Report(new GameDataExtractionProgress(value));
            try
            {
                if (request.Source == GameDataExtractionSource.Ps2Iso)
                {
                    if (string.IsNullOrWhiteSpace(request.IsoPath) || request.IsoGame == null)
                        return new GameDataExtractionResult(OperationOutcome.Failure(
                            OperationFailureKind.InvalidRequest, "A PS2 ISO path and game identifier are required."));

                    switch (request.IsoGame.Value)
                    {
                        case WizardGameId.KingdomHearts1:
                            await ExtractKh1Ps2EditionAsync(request.IsoPath, request.DestinationPath, report, cancellationToken);
                            break;
                        case WizardGameId.KingdomHearts2:
                            await ExtractKh2Ps2EditionAsync(request.IsoPath, request.DestinationPath, report, cancellationToken);
                            break;
                        case WizardGameId.ReChainOfMemories:
                            await ExtractRecomPs2EditionAsync(request.IsoPath, request.DestinationPath, report, cancellationToken);
                            break;
                        default:
                            return new GameDataExtractionResult(OperationOutcome.Failure(
                                OperationFailureKind.Unsupported, "The selected game is not supported for PS2 ISO extraction."));
                    }
                }
                else
                {
                    var language = string.IsNullOrWhiteSpace(request.PcLanguageFolder) ? "en" : request.PcLanguageFolder;
                    await ExtractKhPcEditionAsync(
                        request.DestinationPath,
                        report,
                        file => Path.Combine(request.Pc1525Path, "Image", language, file),
                        file => Path.Combine(request.Pc28Path, "Image", language, file),
                        request.ExtractKh1,
                        request.ExtractKh2,
                        request.ExtractBbs,
                        request.ExtractRecom,
                        request.ExtractKh3d,
                        request.RetryAsync ?? (_ => Task.FromResult(false)),
                        cancellationToken);
                }

                return new GameDataExtractionResult(OperationOutcome.Success(changed: true));
            }
            catch (OperationCanceledException)
            {
                return new GameDataExtractionResult(OperationOutcome.Failure(
                    OperationFailureKind.Cancelled, "Extraction was cancelled."));
            }
            catch (BadConfigurationException ex)
            {
                return new GameDataExtractionResult(OperationOutcome.Failure(OperationFailureKind.InvalidData, ex.Message));
            }
            catch (IOException ex)
            {
                return new GameDataExtractionResult(OperationOutcome.Failure(OperationFailureKind.FileSystem, ex.Message));
            }
            catch (Exception ex)
            {
                return new GameDataExtractionResult(OperationOutcome.Failure(OperationFailureKind.Unexpected, ex.Message));
            }
        }
    }
}
