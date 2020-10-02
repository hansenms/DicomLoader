# DICOM Loader

A simple tool for loading DICOM images into the [Microsoft DICOM server for Azure](https://github.com/Microsoft/dicom-server).

To use it, upload the DICOM files to Azure Blob Storage and login in with the Azure CLI:

```bash
az login
```

Then load files with:

```
dotnet run -- --blob-container-url https://<storageaccount>.blob.core.windows.net/<container> --blob-prefix path/to/dicoms --dicom-server-url https://<mydicomserver>.azurewebsites.net
```

Command line options:

```
Usage:
  DicomLoader [options]

Options:
  --blob-container-url <blob-container-url>                  blobContainerUrl
  --dicom-server-url <dicom-server-url>                      dicomServerUrl
  --blob-prefix <blob-prefix>                                blobPrefix [default: ]
  --max-degree-of-parallelism <max-degree-of-parallelism>    maxDegreeOfParallelism [default: 8]
  --refresh-interval <refresh-interval>                      refreshInterval [default: 5]
  --version                                                  Show version information
  -?, -h, --help                                             Show help and usage information
  ```

> Note: This loader does **not support** authentication to the DICOM service (yet)
