﻿namespace BlobConfigurationProvider;

public class BlobConfigurationOptions
{
    public string ConnectionString { get; set; } = string.Empty;
    public string ContainerName { get; set; } = string.Empty;
    public string BlobName { get; set; } = string.Empty;

    public bool Optional { get; set; }
    public bool ReloadOnChange { get; set; }
    public int ReloadInterval { get; set; } = 30000;
}