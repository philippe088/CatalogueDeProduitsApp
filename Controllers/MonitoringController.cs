using Microsoft.AspNetCore.Mvc;
using CatalogueDeProduitsApp.Services;
using CatalogueDeProduitsApp.Services.Interfaces;
using System.IO;

namespace CatalogueDeProduitsApp.Controllers
{
    /// <summary>
    /// Contrôleur pour le monitoring et les métriques
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    public class MonitoringController : ControllerBase
    {
        private readonly CsvMonitoringService _monitoring;
        private readonly IProduitRepository _repository;
        private readonly ILogger<MonitoringController> _logger;
        private readonly IWebHostEnvironment _environment;

        public MonitoringController(
            CsvMonitoringService monitoring, 
            IProduitRepository repository,
            ILogger<MonitoringController> logger,
            IWebHostEnvironment environment)
        {
            _monitoring = monitoring;
            _repository = repository;
            _logger = logger;
            _environment = environment;
        }

        /// <summary>
        /// Vérifie la santé du système
        /// </summary>
        [HttpGet("health")]
        public async Task<IActionResult> GetHealth()
        {
            try
            {
                var csvPath = Path.Combine(_environment.ContentRootPath, "Data", "produits.csv");
                var healthCheck = await _monitoring.CheckFileSystemHealthAsync(csvPath);
                
                var overallHealth = new
                {
                    IsHealthy = healthCheck.IsHealthy,
                    Timestamp = DateTime.UtcNow,
                    Application = "Catalogue de Produits",
                    Environment = _environment.EnvironmentName,
                    Checks = new[]
                    {
                        healthCheck
                    }
                };

                return healthCheck.IsHealthy 
                    ? Ok(overallHealth) 
                    : StatusCode(503, overallHealth);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors du health check");
                return StatusCode(500, new { Message = "Erreur interne lors du health check" });
            }
        }

        /// <summary>
        /// Récupère les métriques de performance
        /// </summary>
        [HttpGet("metrics")]
        public async Task<IActionResult> GetMetrics()
        {
            try
            {
                var metrics = await _monitoring.MeasureOperationAsync("GetMetrics", async () =>
                {
                    return await Task.Run(() => _monitoring.GetMetrics());
                });

                return Ok(metrics);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors de la récupération des métriques");
                return StatusCode(500, new { Message = "Erreur interne lors de la récupération des métriques" });
            }
        }

        /// <summary>
        /// Test de performance sur les opérations CRUD
        /// </summary>
        [HttpPost("performance-test")]
        public async Task<IActionResult> RunPerformanceTest([FromBody] PerformanceTestRequest request)
        {
            try
            {
                var testResults = new List<object>();
                
                // Test de lecture
                var readResult = await _monitoring.MeasureOperationAsync("PerformanceTest_Read", async () =>
                {
                    var produits = await _repository.GetAllAsync();
                    return produits.Count();
                });

                testResults.Add(new
                {
                    Operation = "Read All Products",
                    ProductCount = readResult,
                    Status = "Success"
                });

                // Test de recherche
                if (!string.IsNullOrEmpty(request.SearchTerm))
                {
                    var searchResult = await _monitoring.MeasureOperationAsync("PerformanceTest_Search", async () =>
                    {
                        var results = await _repository.SearchByNameAsync(request.SearchTerm);
                        return results.Count();
                    });

                    testResults.Add(new
                    {
                        Operation = $"Search '{request.SearchTerm}'",
                        ResultCount = searchResult,
                        Status = "Success"
                    });
                }

                // Test de pagination
                var paginationResult = await _monitoring.MeasureOperationAsync("PerformanceTest_Pagination", async () =>
                {
                    var pagedResult = await _repository.GetPagedAsync(1, request.PageSize);
                    return pagedResult.Items.Count();
                });

                testResults.Add(new
                {
                    Operation = $"Pagination (Page 1, Size {request.PageSize})",
                    ItemCount = paginationResult,
                    Status = "Success"
                });

                return Ok(new
                {
                    TestCompleted = DateTime.UtcNow,
                    Results = testResults,
                    Metrics = _monitoring.GetMetrics()
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors du test de performance");
                return StatusCode(500, new { Message = "Erreur interne lors du test de performance" });
            }
        }

        /// <summary>
        /// Diagnostic complet du système
        /// </summary>
        [HttpGet("diagnostics")]
        public async Task<IActionResult> GetDiagnostics()
        {
            try
            {
                var csvPath = Path.Combine(_environment.ContentRootPath, "Data", "produits.csv");
                var diagnostics = new
                {
                    Timestamp = DateTime.UtcNow,
                    Application = new
                    {
                        Name = "Catalogue de Produits",
                        Environment = _environment.EnvironmentName,
                        ContentRoot = _environment.ContentRootPath,
                        WebRoot = _environment.WebRootPath
                    },
                    Database = new
                    {
                        Type = "CSV File",
                        Path = csvPath,
                        Exists = System.IO.File.Exists(csvPath),
                        Size = System.IO.File.Exists(csvPath) ? new FileInfo(csvPath).Length : 0,
                        LastModified = System.IO.File.Exists(csvPath) ? new FileInfo(csvPath).LastWriteTime : (DateTime?)null
                    },
                    Performance = _monitoring.GetMetrics(),
                    DataIntegrity = await CheckDataIntegrityAsync()
                };

                return Ok(diagnostics);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors du diagnostic");
                return StatusCode(500, new { Message = "Erreur interne lors du diagnostic" });
            }
        }

        /// <summary>
        /// Nettoie les anciennes sauvegardes et fichiers temporaires
        /// </summary>
        [HttpPost("cleanup")]
        public async Task<IActionResult> Cleanup()
        {
            try
            {
                var cleanupResults = await _monitoring.MeasureOperationAsync("Cleanup", async () =>
                {
                    return await Task.Run(() =>
                    {
                        var dataDirectory = Path.Combine(_environment.ContentRootPath, "Data");
                        var cleanedFiles = new List<string>();
                        var errors = new List<string>();

                        if (Directory.Exists(dataDirectory))
                        {
                            // Nettoyer les fichiers de sauvegarde anciens (plus de 7 jours)
                            var cutoffDate = DateTime.Now.AddDays(-7);                        var backupFiles = Directory.GetFiles(dataDirectory, "*.bak")
                                                      .Concat(Directory.GetFiles(dataDirectory, "*.backup_*"))
                                                      .Where(f => System.IO.File.GetCreationTime(f) < cutoffDate);

                            foreach (var file in backupFiles)
                            {
                                try
                                {
                                    System.IO.File.Delete(file);
                                    cleanedFiles.Add(Path.GetFileName(file));
                                }
                                catch (Exception ex)
                                {
                                    errors.Add($"Impossible de supprimer {Path.GetFileName(file)}: {ex.Message}");
                                }
                            }

                            // Nettoyer les fichiers temporaires
                            var tempFiles = Directory.GetFiles(dataDirectory, "*.tmp")
                                                    .Concat(Directory.GetFiles(dataDirectory, "*.temp"));

                            foreach (var file in tempFiles)
                            {
                                try
                                {
                                    System.IO.File.Delete(file);
                                    cleanedFiles.Add(Path.GetFileName(file));
                                }
                                catch (Exception ex)
                                {
                                    errors.Add($"Impossible de supprimer {Path.GetFileName(file)}: {ex.Message}");
                                }
                            }
                        }

                        return new { CleanedFiles = cleanedFiles, Errors = errors };
                    });
                });

                return Ok(new
                {
                    Message = "Nettoyage terminé",
                    CleanedFiles = cleanupResults.CleanedFiles,
                    Errors = cleanupResults.Errors,
                    CleanupTime = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors du nettoyage");
                return StatusCode(500, new { Message = "Erreur interne lors du nettoyage" });
            }
        }

        private async Task<object> CheckDataIntegrityAsync()
        {
            try
            {
                var produits = await _repository.GetAllAsync();
                var produitsArray = produits.ToArray();

                var integrity = new
                {
                    TotalProducts = produitsArray.Length,
                    UniqueIds = produitsArray.Select(p => p.Id).Distinct().Count(),
                    DuplicateIds = produitsArray.Length - produitsArray.Select(p => p.Id).Distinct().Count(),
                    FeaturedProducts = produitsArray.Count(p => p.Vedette),
                    ProductsWithoutName = produitsArray.Count(p => string.IsNullOrWhiteSpace(p.Nom)),
                    ProductsWithoutDescription = produitsArray.Count(p => string.IsNullOrWhiteSpace(p.Description)),
                    ProductsWithZeroPrice = produitsArray.Count(p => p.Prix <= 0),
                    ProductsWithNegativeQuantity = produitsArray.Count(p => p.Quantite < 0),
                    IsValid = true
                };

                // Marquer comme invalide si des problèmes sont détectés
                var hasIssues = integrity.DuplicateIds > 0 || 
                               integrity.FeaturedProducts > 1 || 
                               integrity.ProductsWithoutName > 0 ||
                               integrity.ProductsWithZeroPrice > 0 ||
                               integrity.ProductsWithNegativeQuantity > 0;

                return new
                {
                    integrity.TotalProducts,
                    integrity.UniqueIds,
                    integrity.DuplicateIds,
                    integrity.FeaturedProducts,
                    integrity.ProductsWithoutName,
                    integrity.ProductsWithoutDescription,
                    integrity.ProductsWithZeroPrice,
                    integrity.ProductsWithNegativeQuantity,
                    IsValid = !hasIssues
                };
            }
            catch (Exception ex)
            {
                return new
                {
                    Error = ex.Message,
                    IsValid = false
                };
            }
        }
    }

    public class PerformanceTestRequest
    {
        public string? SearchTerm { get; set; }
        public int PageSize { get; set; } = 10;
    }
}
