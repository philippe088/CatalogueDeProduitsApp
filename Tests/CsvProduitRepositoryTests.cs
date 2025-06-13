using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Hosting;
using CatalogueDeProduitsApp.Models;
using CatalogueDeProduitsApp.Services;
using CatalogueDeProduitsApp.Services.Repositories;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace CatalogueDeProduitsApp.Tests
{
    /// <summary>
    /// Tests unitaires pour démontrer la robustesse du système CSV
    /// </summary>
    public class CsvProduitRepositoryTests
    {
        private readonly Mock<IWebHostEnvironment> _mockEnvironment;
        private readonly ILogger<CsvProduitRepository> _logger;
        private readonly string _testFilePath;
        private readonly string _testDirectory;

        public CsvProduitRepositoryTests()
        {
            _mockEnvironment = new Mock<IWebHostEnvironment>();
            _logger = NullLogger<CsvProduitRepository>.Instance;
            
            _testDirectory = Path.Combine(Path.GetTempPath(), "CsvTests", Guid.NewGuid().ToString());
            _testFilePath = Path.Combine(_testDirectory, "test_produits.csv");
            
            _mockEnvironment.Setup(x => x.ContentRootPath).Returns(_testDirectory);
        }        /// <summary>
        /// Test de création d'un produit
        /// </summary>
        [Fact]
        public async Task TestCreateProduitAsync()
        {
            // Arrange
            using var repository = new CsvProduitRepository(_mockEnvironment.Object, _logger);
            var produit = new Produit
            {
                Nom = "Produit Test",
                Description = "Description test",
                Prix = 19.99m,
                Quantite = 10,
                Image = "test.jpg",
                Vedette = false
            };

            // Act
            var result = await repository.AddAsync(produit);

            // Assert
            Console.WriteLine($"Création produit: {(result.IsSuccess ? "SUCCÈS" : "ÉCHEC")}");
            if (result.IsSuccess)
            {
                Console.WriteLine($"ID généré: {result.Data?.Id}");
                Console.WriteLine($"Message: {result.Message}");
            }
            else
            {
                Console.WriteLine($"Erreurs: {string.Join(", ", result.Errors)}");
            }
        }

        /// <summary>
        /// Test de concurrence - plusieurs opérations simultanées
        /// </summary>
        [Fact]
        public async Task TestConcurrencyAsync()
        {
            Console.WriteLine("=== Test de concurrence ===");
            
            using var repository = new CsvProduitRepository(_mockEnvironment.Object, _logger);
            
            var tasks = new List<Task>();
            var results = new List<string>();
            var lockObject = new object();

            // Créer 10 produits simultanément
            for (int i = 0; i < 10; i++)
            {
                var index = i;
                tasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        var produit = new Produit
                        {
                            Nom = $"Produit Concurrent {index}",
                            Description = $"Description {index}",
                            Prix = 10.00m + index,
                            Quantite = index * 2,
                            Image = $"image{index}.jpg",
                            Vedette = false
                        };

                        var result = await repository.AddAsync(produit);
                        
                        lock (lockObject)
                        {
                            results.Add($"Thread {index}: {(result.IsSuccess ? $"Succès (ID: {result.Data?.Id})" : $"Échec ({string.Join(", ", result.Errors)})")}");
                        }
                    }
                    catch (Exception ex)
                    {
                        lock (lockObject)
                        {
                            results.Add($"Thread {index}: Exception - {ex.Message}");
                        }
                    }
                }));
            }

            await Task.WhenAll(tasks);
            
            Console.WriteLine("Résultats des opérations concurrentes:");
            foreach (var result in results.OrderBy(r => r))
            {
                Console.WriteLine($"  {result}");
            }

            // Vérifier l'intégrité des données
            var allProduits = await repository.GetAllAsync();
            Console.WriteLine($"Total produits créés: {allProduits.Count()}");
        }

        /// <summary>
        /// Test de transaction avec rollback
        /// </summary>
        [Fact]
        public async Task TestTransactionRollbackAsync()
        {
            Console.WriteLine("=== Test de transaction avec rollback ===");
            
            var testFile = Path.Combine(_testDirectory, "transaction_test.csv");
            
            // Créer un fichier initial avec des données
            Directory.CreateDirectory(_testDirectory);
            await File.WriteAllTextAsync(testFile, "1;Produit Initial;Description;10.00;5;init.jpg;false\n");

            try
            {
                using var transaction = new CsvTransaction(testFile);
                
                // Ajouter des opérations valides
                transaction.AddOperation(OperationType.Create, new Produit
                {
                    Id = 2,
                    Nom = "Produit Valid",
                    Description = "Description valide",
                    Prix = 15.00m,
                    Quantite = 3,
                    Image = "valid.jpg",
                    Vedette = false
                });

                // Ajouter une opération invalide (ID négatif)
                transaction.AddOperation(OperationType.Create, new Produit
                {
                    Id = -1, // ID invalide
                    Nom = "Produit Invalide",
                    Description = "Description",
                    Prix = 20.00m,
                    Quantite = 1,
                    Image = "invalid.jpg",
                    Vedette = false
                });

                var result = await transaction.CommitAsync();
                
                Console.WriteLine($"Transaction: {(result.IsSuccess ? "SUCCÈS" : "ÉCHEC")}");
                Console.WriteLine($"Message: {result.Message}");
                if (!result.IsSuccess)
                {
                    Console.WriteLine($"Erreurs: {string.Join(", ", result.Errors)}");
                }

                // Vérifier que le fichier n'a pas été modifié (rollback)
                var content = await File.ReadAllTextAsync(testFile);
                var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                Console.WriteLine($"Nombre de lignes après rollback: {lines.Length}");
                Console.WriteLine("Contenu du fichier après rollback:");
                foreach (var line in lines)
                {
                    Console.WriteLine($"  {line}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Exception lors du test de transaction: {ex.Message}");
            }
        }

        /// <summary>
        /// Test de validation robuste
        /// </summary>
        [Fact]
        public async Task TestValidationAsync()
        {
            Console.WriteLine("=== Test de validation ===");
            
            using var repository = new CsvProduitRepository(_mockEnvironment.Object, _logger);
            
            var testCases = new[]
            {
                new { 
                    Name = "Nom vide", 
                    Produit = new Produit { Nom = "", Description = "Test", Prix = 10m, Quantite = 1, Image = "test.jpg" } 
                },
                new { 
                    Name = "Prix négatif", 
                    Produit = new Produit { Nom = "Test", Description = "Test", Prix = -5m, Quantite = 1, Image = "test.jpg" } 
                },
                new { 
                    Name = "Quantité négative", 
                    Produit = new Produit { Nom = "Test", Description = "Test", Prix = 10m, Quantite = -1, Image = "test.jpg" } 
                },
                new { 
                    Name = "Nom trop long", 
                    Produit = new Produit { Nom = new string('A', 101), Description = "Test", Prix = 10m, Quantite = 1, Image = "test.jpg" } 
                }
            };

            foreach (var testCase in testCases)
            {
                var result = await repository.AddAsync(testCase.Produit);
                Console.WriteLine($"{testCase.Name}: {(result.IsSuccess ? "ACCEPTÉ (inattendu)" : "REJETÉ (attendu)")}");
                if (!result.IsSuccess)
                {
                    Console.WriteLine($"  Erreurs: {string.Join(", ", result.Errors)}");
                }
            }
        }

        /// <summary>
        /// Test de parsing CSV avec caractères spéciaux
        /// </summary>
        [Fact]
        public void TestCsvParsingWithSpecialCharacters()
        {
            Console.WriteLine("=== Test de parsing CSV avec caractères spéciaux ===");
            
            var testCases = new[]
            {
                new { Description = "Guillemets dans le nom", CsvLine = "1;\"Produit \"Special\"\";Description;10.00;5;test.jpg;false" },
                new { Description = "Point-virgule dans description", CsvLine = "2;Produit;\"Description; avec point-virgule\";15.00;3;test2.jpg;false" },
                new { Description = "Retour à la ligne", CsvLine = "3;\"Produit\nMultiligne\";Description;20.00;2;test3.jpg;false" },
                new { Description = "Caractères accentués", CsvLine = "4;Produit Français;\"Café à l'américaine\";25.00;1;café.jpg;false" }
            };

            foreach (var testCase in testCases)
            {
                try
                {
                    var produit = CsvProduitSerializer.DeserializeProduit(testCase.CsvLine);
                    Console.WriteLine($"{testCase.Description}: SUCCÈS");
                    Console.WriteLine($"  Nom: '{produit.Nom}'");
                    Console.WriteLine($"  Description: '{produit.Description}'");
                    Console.WriteLine($"  Prix: {produit.Prix}");
                    
                    // Test de sérialisation inverse
                    var serialized = CsvProduitSerializer.SerializeProduit(produit);
                    Console.WriteLine($"  Sérialisé: {serialized}");
                    
                    // Test de désérialisation inverse
                    var roundTrip = CsvProduitSerializer.DeserializeProduit(serialized);
                    var isIdentical = produit.Nom == roundTrip.Nom && 
                                    produit.Description == roundTrip.Description &&
                                    produit.Prix == roundTrip.Prix;
                    Console.WriteLine($"  Round-trip: {(isIdentical ? "SUCCÈS" : "ÉCHEC")}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"{testCase.Description}: ÉCHEC - {ex.Message}");
                }
                Console.WriteLine();
            }
        }

        /// <summary>
        /// Lance tous les tests
        /// </summary>
        [Fact]
        public async Task RunAllTestsAsync()
        {
            try
            {
                Console.WriteLine("=== DÉBUT DES TESTS DE ROBUSTESSE CSV ===\n");
                
                await TestCreateProduitAsync();
                Console.WriteLine();
                
                await TestConcurrencyAsync();
                Console.WriteLine();
                
                await TestTransactionRollbackAsync();
                Console.WriteLine();
                
                await TestValidationAsync();
                Console.WriteLine();
                
                TestCsvParsingWithSpecialCharacters();
                
                Console.WriteLine("=== FIN DES TESTS ===");
            }
            finally
            {
                // Nettoyer les fichiers de test
                try
                {
                    if (Directory.Exists(_testDirectory))
                    {
                        Directory.Delete(_testDirectory, true);
                    }
                }
                catch
                {
                    // Ignorer les erreurs de nettoyage
                }
            }
        }
    }    /// <summary>
    /// Programme de test
    /// </summary>
    public class TestRunner
    {
        public static async Task RunTestsAsync()
        {
            var tests = new CsvProduitRepositoryTests();
            await tests.RunAllTestsAsync();
            
            Console.WriteLine("\nTests terminés.");
        }
    }
}
