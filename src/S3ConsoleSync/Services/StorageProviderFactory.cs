using S3ConsoleSync.Models;
using S3ConsoleSync.Services.Providers;

namespace S3ConsoleSync.Services;

/// <summary>
/// Creates the appropriate <see cref="IStorageProvider"/> from a
/// <see cref="SyncConfig"/>.
/// </summary>
public static class StorageProviderFactory
{
    public static IStorageProvider Create(SyncConfig config) =>
        config.Provider switch
        {
            StorageProviderType.S3        => new AwsS3Provider(config.Credentials, config.Region),
            StorageProviderType.Wasabi    => new WasabiProvider(config.Credentials, config.Region),
            StorageProviderType.AzureBlob => new AzureBlobProvider(config.Credentials),
            _                             => throw new NotSupportedException($"Unknown provider: {config.Provider}")
        };
}
