using System.Diagnostics;
using System.Text.Json;

namespace CatalogueDeProduitsApp.Services
{
    /// <summary>
    /// Service de monitoring pour les opérations CSV
    /// </summary>
    public class CsvMonitoringService
    {
        private readonly ILogger<CsvMonitoringService> _logger;
        private readonly string _metricsPath;
        private readonly Timer _metricsTimer;
        private readonly ConcurrentMetrics _metrics;

        public CsvMonitoringService(ILogger<CsvMonitoringService> logger, IWebHostEnvironment environment)
        {
            _logger = logger;
            _metricsPath = Path.Combine(environment.ContentRootPath, "Logs", "metrics.json");
            _metrics = new ConcurrentMetrics();

            // Sauvegarder les métriques toutes les 5 minutes
            _metricsTimer = new Timer(SaveMetrics, null, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
        }

        /// <summary>
        /// Mesure le temps d'exécution d'une opération
        /// </summary>
        public async Task<T> MeasureOperationAsync<T>(string operationName, Func<Task<T>> operation)
        {
            var stopwatch = Stopwatch.StartNew();
            var startTime = DateTime.UtcNow;

            try
            {
                _logger.LogInformation("Début de l'opération: {OperationName}", operationName);
                
                var result = await operation();
                
                stopwatch.Stop();
                _metrics.RecordSuccess(operationName, stopwatch.ElapsedMilliseconds);
                
                _logger.LogInformation("Opération {OperationName} réussie en {ElapsedMs}ms", 
                    operationName, stopwatch.ElapsedMilliseconds);
                
                return result;
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                _metrics.RecordFailure(operationName, stopwatch.ElapsedMilliseconds);
                
                _logger.LogError(ex, "Opération {OperationName} échouée après {ElapsedMs}ms", 
                    operationName, stopwatch.ElapsedMilliseconds);
                
                throw;
            }
        }

        /// <summary>
        /// Enregistre une erreur personnalisée
        /// </summary>
        public void RecordError(string operation, string errorType, string message)
        {
            _metrics.RecordCustomError(operation, errorType, message);
            _logger.LogError("Erreur personnalisée - Opération: {Operation}, Type: {ErrorType}, Message: {Message}", 
                operation, errorType, message);
        }

        /// <summary>
        /// Obtient les métriques actuelles
        /// </summary>
        public MetricsSnapshot GetMetrics()
        {
            return _metrics.GetSnapshot();
        }

        /// <summary>
        /// Vérifie la santé du système de fichiers
        /// </summary>
        public async Task<HealthCheckResult> CheckFileSystemHealthAsync(string filePath)
        {
            var result = new HealthCheckResult
            {
                CheckName = "FileSystem",
                Timestamp = DateTime.UtcNow
            };

            try
            {
                // Vérifier l'accès au répertoire
                var directory = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(directory))
                {
                    var dirInfo = new DirectoryInfo(directory);
                    result.IsHealthy = dirInfo.Exists;
                    
                    if (result.IsHealthy)
                    {
                        // Vérifier l'espace disque disponible
                        var drive = new DriveInfo(dirInfo.Root.FullName);
                        var freeSpaceGB = drive.AvailableFreeSpace / (1024.0 * 1024.0 * 1024.0);
                        
                        result.Details["FreeSpaceGB"] = freeSpaceGB.ToString("F2");
                        result.Details["TotalSpaceGB"] = (drive.TotalSize / (1024.0 * 1024.0 * 1024.0)).ToString("F2");
                        
                        if (freeSpaceGB < 1.0) // Moins de 1GB disponible
                        {
                            result.IsHealthy = false;
                            result.Message = "Espace disque insuffisant";
                        }
                    }
                    else
                    {
                        result.Message = "Répertoire inaccessible";
                    }
                }

                // Vérifier l'accès au fichier
                if (result.IsHealthy && File.Exists(filePath))
                {
                    var fileInfo = new FileInfo(filePath);
                    result.Details["FileSizeKB"] = (fileInfo.Length / 1024.0).ToString("F2");
                    result.Details["LastModified"] = fileInfo.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss");

                    // Test de lecture/écriture
                    var testContent = $"Health check test - {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}";
                    var testFile = filePath + ".healthcheck";
                    
                    await File.WriteAllTextAsync(testFile, testContent);
                    var readContent = await File.ReadAllTextAsync(testFile);
                    File.Delete(testFile);
                    
                    if (readContent != testContent)
                    {
                        result.IsHealthy = false;
                        result.Message = "Erreur de lecture/écriture";
                    }
                }

                if (result.IsHealthy && string.IsNullOrEmpty(result.Message))
                {
                    result.Message = "Système de fichiers en bon état";
                }
            }
            catch (Exception ex)
            {
                result.IsHealthy = false;
                result.Message = ex.Message;
                result.Details["Exception"] = ex.GetType().Name;
            }

            return result;
        }

        private void SaveMetrics(object? state)
        {
            try
            {
                var snapshot = _metrics.GetSnapshot();
                var json = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions 
                { 
                    WriteIndented = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });

                var directory = Path.GetDirectoryName(_metricsPath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.WriteAllText(_metricsPath, json);
                _logger.LogDebug("Métriques sauvegardées dans {Path}", _metricsPath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors de la sauvegarde des métriques");
            }
        }

        public void Dispose()
        {
            _metricsTimer?.Dispose();
            SaveMetrics(null); // Sauvegarde finale
        }
    }

    /// <summary>
    /// Métriques thread-safe
    /// </summary>
    public class ConcurrentMetrics
    {
        private readonly object _lock = new object();
        private readonly Dictionary<string, OperationMetrics> _operations = new();
        private readonly List<CustomError> _customErrors = new();

        public void RecordSuccess(string operation, long durationMs)
        {
            lock (_lock)
            {
                if (!_operations.ContainsKey(operation))
                {
                    _operations[operation] = new OperationMetrics { Name = operation };
                }

                var metrics = _operations[operation];
                metrics.SuccessCount++;
                metrics.TotalDurationMs += durationMs;
                metrics.LastSuccessTime = DateTime.UtcNow;
                
                if (durationMs > metrics.MaxDurationMs)
                    metrics.MaxDurationMs = durationMs;
                
                if (metrics.MinDurationMs == 0 || durationMs < metrics.MinDurationMs)
                    metrics.MinDurationMs = durationMs;
            }
        }

        public void RecordFailure(string operation, long durationMs)
        {
            lock (_lock)
            {
                if (!_operations.ContainsKey(operation))
                {
                    _operations[operation] = new OperationMetrics { Name = operation };
                }

                var metrics = _operations[operation];
                metrics.FailureCount++;
                metrics.LastFailureTime = DateTime.UtcNow;
            }
        }

        public void RecordCustomError(string operation, string errorType, string message)
        {
            lock (_lock)
            {
                _customErrors.Add(new CustomError
                {
                    Operation = operation,
                    ErrorType = errorType,
                    Message = message,
                    Timestamp = DateTime.UtcNow
                });

                // Garder seulement les 100 dernières erreurs
                if (_customErrors.Count > 100)
                {
                    _customErrors.RemoveAt(0);
                }
            }
        }

        public MetricsSnapshot GetSnapshot()
        {
            lock (_lock)
            {
                return new MetricsSnapshot
                {
                    Operations = _operations.Values.Select(o => new OperationMetrics
                    {
                        Name = o.Name,
                        SuccessCount = o.SuccessCount,
                        FailureCount = o.FailureCount,
                        TotalDurationMs = o.TotalDurationMs,
                        MinDurationMs = o.MinDurationMs,
                        MaxDurationMs = o.MaxDurationMs,
                        LastSuccessTime = o.LastSuccessTime,
                        LastFailureTime = o.LastFailureTime,
                        AverageDurationMs = o.SuccessCount > 0 ? o.TotalDurationMs / o.SuccessCount : 0,
                        SuccessRate = o.TotalCount > 0 ? (double)o.SuccessCount / o.TotalCount * 100 : 0
                    }).ToList(),
                    RecentErrors = _customErrors.TakeLast(20).ToList(),
                    GeneratedAt = DateTime.UtcNow
                };
            }
        }
    }

    // Classes de données
    public class OperationMetrics
    {
        public string Name { get; set; } = string.Empty;
        public long SuccessCount { get; set; }
        public long FailureCount { get; set; }
        public long TotalDurationMs { get; set; }
        public long MinDurationMs { get; set; }
        public long MaxDurationMs { get; set; }
        public DateTime? LastSuccessTime { get; set; }
        public DateTime? LastFailureTime { get; set; }
        public double AverageDurationMs { get; set; }
        public double SuccessRate { get; set; }
        public long TotalCount => SuccessCount + FailureCount;
    }

    public class CustomError
    {
        public string Operation { get; set; } = string.Empty;
        public string ErrorType { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
    }

    public class MetricsSnapshot
    {
        public List<OperationMetrics> Operations { get; set; } = new();
        public List<CustomError> RecentErrors { get; set; } = new();
        public DateTime GeneratedAt { get; set; }
    }

    public class HealthCheckResult
    {
        public string CheckName { get; set; } = string.Empty;
        public bool IsHealthy { get; set; }
        public string Message { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
        public Dictionary<string, string> Details { get; set; } = new();
    }
}
