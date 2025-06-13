using System.Collections.Concurrent;
using System.Text;

namespace CatalogueDeProduitsApp.Services
{    /// <summary>
    /// Gestionnaire de fichier CSV thread-safe avec gestion de la concurrence
    /// </summary>
    public class CsvFileManager : IDisposable
    {
        private readonly string _filePath;
        private readonly SemaphoreSlim _semaphore;
        private readonly object _lockObject = new object();
        private static readonly ConcurrentDictionary<string, SemaphoreSlim> _fileLocks = new();
        private bool _disposed = false;
        
        public CsvFileManager(string filePath)
        {
            _filePath = filePath;
            // Un semaphore par fichier pour éviter les accès concurrents
            _semaphore = _fileLocks.GetOrAdd(_filePath, _ => new SemaphoreSlim(1, 1));
        }        /// <summary>
        /// Lit le fichier CSV de manière thread-safe
        /// </summary>
        public async Task<List<string>> ReadAllLinesAsync()
        {
            // Vérifier si l'objet n'a pas été disposé
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(CsvFileManager));
            }
            
            // Utiliser un timeout pour acquérir le verrou
            bool lockAcquired = false;
            try
            {
                lockAcquired = await _semaphore.WaitAsync(TimeSpan.FromSeconds(3));
                
                if (!lockAcquired)
                {
                    // Si on ne peut pas acquérir le verrou, lire le fichier directement
                    // sans passer par le mécanisme de verrouillage du manager
                    if (!File.Exists(_filePath))
                    {
                        return new List<string>();
                    }
                    
                    try
                    {
                        return new List<string>(await File.ReadAllLinesAsync(_filePath));
                    }
                    catch (Exception ex)
                    {
                        throw new InvalidOperationException($"Impossible de lire le fichier (mode direct): {ex.Message}", ex);
                    }
                }
                
                if (!File.Exists(_filePath))
                {
                    return new List<string>();
                }

                // Lecture avec retry en cas d'erreur
                var retryCount = 2; // Réduit pour éviter les attentes trop longues
                Exception? lastException = null;

                for (int i = 0; i < retryCount; i++)
                {
                    try
                    {
                        // Utilisation de FileShare.Read pour permettre lectures multiples
                        using var stream = new FileStream(_filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                        using var reader = new StreamReader(stream, Encoding.UTF8);
                        
                        var lines = new List<string>();
                        string? line;
                        while ((line = await reader.ReadLineAsync()) != null)
                        {
                            if (!string.IsNullOrWhiteSpace(line))
                            {
                                lines.Add(line);
                            }
                        }
                        return lines;
                    }
                    catch (IOException ex) when (i < retryCount - 1)
                    {
                        lastException = ex;
                        await Task.Delay(50 * (i + 1)); // Délai réduit
                    }
                }

                throw new InvalidOperationException($"Impossible de lire le fichier après {retryCount} tentatives", lastException);
            }
            catch (ObjectDisposedException)
            {
                // Si le semaphore a été disposé, essayer de lire directement
                if (File.Exists(_filePath))
                {
                    return new List<string>(await File.ReadAllLinesAsync(_filePath));
                }
                return new List<string>();
            }
            finally
            {
                if (lockAcquired && !_disposed)
                {
                    _semaphore.Release();
                }
            }
        }        /// <summary>
        /// Écrit dans le fichier CSV de manière atomique
        /// </summary>
        public async Task WriteAllLinesAsync(IEnumerable<string> lines)
        {
            // Vérifier si l'objet n'a pas été disposé
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(CsvFileManager));
            }
              bool lockAcquired = false;
            try
            {
                await _semaphore.WaitAsync();
                lockAcquired = true;
                
                // Créer le répertoire si nécessaire
                var directory = Path.GetDirectoryName(_filePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                // Écriture atomique : écrire dans un fichier temporaire puis renommer
                var tempFile = _filePath + ".tmp";
                var backupFile = _filePath + ".bak";

                try
                {
                    // Créer une sauvegarde de l'ancien fichier
                    if (File.Exists(_filePath))
                    {
                        File.Copy(_filePath, backupFile, true);
                    }

                    // Écrire dans le fichier temporaire
                    using (var stream = new FileStream(tempFile, FileMode.Create, FileAccess.Write, FileShare.None))
                    using (var writer = new StreamWriter(stream, Encoding.UTF8))
                    {
                        foreach (var line in lines)
                        {
                            await writer.WriteLineAsync(line);
                        }
                        await writer.FlushAsync();
                    }

                    // Remplacer l'ancien fichier par le nouveau (opération atomique)
                    if (File.Exists(_filePath))
                    {
                        File.Delete(_filePath);
                    }
                    File.Move(tempFile, _filePath);

                    // Supprimer la sauvegarde si tout s'est bien passé
                    if (File.Exists(backupFile))
                    {
                        File.Delete(backupFile);
                    }
                }
                catch (Exception)
                {
                    // En cas d'erreur, nettoyer et restaurer
                    if (File.Exists(tempFile))
                    {
                        File.Delete(tempFile);
                    }

                    if (File.Exists(backupFile) && !File.Exists(_filePath))
                    {
                        File.Move(backupFile, _filePath);
                    }
                    throw;
                }
            }
            catch (ObjectDisposedException)
            {
                throw new ObjectDisposedException(nameof(CsvFileManager));
            }
            finally
            {
                if (lockAcquired && !_disposed)
                {
                    _semaphore.Release();
                }
            }
        }        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                // Ne pas disposer le semaphore car il est partagé dans le dictionnaire statique
                // _semaphore?.Dispose();
            }
        }
    }
}
