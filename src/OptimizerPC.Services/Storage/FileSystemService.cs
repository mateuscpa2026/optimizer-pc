using OptimizerPC.Core.Abstractions;

namespace OptimizerPC.Services.Storage;

/// <summary>
/// Acesso ao sistema de arquivos. Nao aplica regras de seguranca: quem decide o que pode
/// ser removido e o SafePathValidator, usado pelos servicos de limpeza.
/// </summary>
public sealed class FileSystemService : IFileSystemService
{
    private static readonly EnumerationOptions RecursiveOptions = new()
    {
        RecurseSubdirectories = true,
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.ReparsePoint,
        ReturnSpecialDirectories = false
    };

    private static readonly EnumerationOptions FlatOptions = new()
    {
        RecurseSubdirectories = false,
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.ReparsePoint,
        ReturnSpecialDirectories = false
    };

    public bool DirectoryExists(string path)
    {
        try
        {
            return Directory.Exists(path);
        }
        catch (Exception)
        {
            return false;
        }
    }

    public bool FileExists(string path)
    {
        try
        {
            return File.Exists(path);
        }
        catch (Exception)
        {
            return false;
        }
    }

    public IEnumerable<string> EnumerateDirectories(string root)
    {
        if (!Directory.Exists(root))
        {
            return Array.Empty<string>();
        }

        try
        {
            return Directory.EnumerateDirectories(root, "*", FlatOptions).ToArray();
        }
        catch (Exception)
        {
            return Array.Empty<string>();
        }
    }

    public IEnumerable<FileEntry> EnumerateFiles(string root, bool recursive)
    {
        if (!Directory.Exists(root))
        {
            return Array.Empty<FileEntry>();
        }

        var entries = new List<FileEntry>();
        var options = recursive ? RecursiveOptions : FlatOptions;

        try
        {
            foreach (var path in Directory.EnumerateFiles(root, "*", options))
            {
                var info = TryRead(path);
                if (info is not null)
                {
                    entries.Add(info);
                }
            }
        }
        catch (Exception)
        {
            // Pastas inacessiveis sao simplesmente ignoradas na varredura.
        }

        return entries;
    }

    public IEnumerable<string> EnumerateFilesByPattern(string root, string searchPattern, bool recursive)
    {
        if (!Directory.Exists(root))
        {
            return Array.Empty<string>();
        }

        var options = recursive ? RecursiveOptions : FlatOptions;

        try
        {
            return Directory.EnumerateFiles(root, searchPattern, options).ToArray();
        }
        catch (Exception)
        {
            return Array.Empty<string>();
        }
    }

    public long GetFileSize(string path)
    {
        try
        {
            return new FileInfo(path).Length;
        }
        catch (Exception)
        {
            return 0;
        }
    }

    public DateTime GetLastWriteTimeUtc(string path)
    {
        try
        {
            return File.GetLastWriteTimeUtc(path);
        }
        catch (Exception)
        {
            return DateTime.MinValue;
        }
    }

    public FileAttributes GetAttributes(string path)
    {
        try
        {
            return File.GetAttributes(path);
        }
        catch (Exception)
        {
            return FileAttributes.Normal;
        }
    }

    public void DeleteFile(string path)
    {
        var attributes = GetAttributes(path);
        if (attributes.HasFlag(FileAttributes.ReadOnly))
        {
            File.SetAttributes(path, attributes & ~FileAttributes.ReadOnly);
        }

        File.Delete(path);
    }

    public void DeleteDirectory(string path, bool recursive)
    {
        Directory.Delete(path, recursive);
    }

    public void CreateDirectory(string path) => Directory.CreateDirectory(path);

    public Stream OpenRead(string path) => new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 64 * 1024, FileOptions.SequentialScan);

    public long GetAvailableFreeSpace(string path)
    {
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(path));
            if (string.IsNullOrEmpty(root))
            {
                return 0;
            }

            return new DriveInfo(root).AvailableFreeSpace;
        }
        catch (Exception)
        {
            return 0;
        }
    }

    public long GetTotalSize(string path)
    {
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(path));
            if (string.IsNullOrEmpty(root))
            {
                return 0;
            }

            return new DriveInfo(root).TotalSize;
        }
        catch (Exception)
        {
            return 0;
        }
    }

    private static FileEntry? TryRead(string path)
    {
        try
        {
            var info = new FileInfo(path);
            return new FileEntry(info.FullName, info.Length, info.LastWriteTimeUtc);
        }
        catch (Exception)
        {
            return null;
        }
    }
}
