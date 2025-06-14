using CatalogueDeProduitsApp.Models;
using CatalogueDeProduitsApp.Services.Interfaces;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Globalization;
using System.Linq;

namespace CatalogueDeProduitsApp.Services.Repositories
{
    /// <summary>
    /// Implémentation du repository de produits utilisant le stockage CSV
    /// </summary>
    public class CsvProduitRepository : IProduitRepository, IDisposable
    {
        private readonly ILogger<CsvProduitRepository> _logger;
        private readonly string _csvFilePath;
        private readonly CsvFileManager _fileManager;        
        private readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);
        private int _lastId = 0;
        private bool _disposed = false;
        private bool _initialized = false;
        private readonly CsvTransaction _transaction;        /// <summary>
        /// Constructeur
        /// </summary>
        public CsvProduitRepository(IWebHostEnvironment environment, ILogger<CsvProduitRepository> logger)
        {
            _logger = logger;
            
            // Chemin vers le fichier CSV
            string dataDirectory = Path.Combine(environment.ContentRootPath, "Data");
            Directory.CreateDirectory(dataDirectory); // Assure que le répertoire existe
            _csvFilePath = Path.Combine(dataDirectory, "produits.csv");
            
            _fileManager = new CsvFileManager(_csvFilePath);
            _transaction = new CsvTransaction(_csvFilePath);
        }        /// <summary>
        /// Initialise le dernier ID en parcourant tous les produits (lazy initialization)
        /// </summary>
        private async Task EnsureInitializedAsync()
        {
            if (_initialized || _disposed) return;
            
            try
            {
                var produits = await GetAllInternalAsync();
                _lastId = produits.Any() ? produits.Max(p => p.Id) : 0;
                _initialized = true;
                _logger.LogInformation("Dernier ID initialisé à {LastId}", _lastId);
            }
            catch (ObjectDisposedException)
            {
                _logger.LogWarning("Tentative d'initialisation du dernier ID sur un objet disposé");
                _lastId = 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors de l'initialisation du dernier ID");
                _lastId = 0; // Par défaut
            }
        }

        /// <summary>
        /// Initialise le dernier ID en parcourant tous les produits
        /// </summary>
        private async Task InitializeLastIdAsync()
        {
            await EnsureInitializedAsync();
        }

          private async Task<IEnumerable<Produit>> GetAllInternalAsync()
        {
            _logger.LogDebug("Début de GetAllInternalAsync");
            
            // Vérifier si l'objet n'a pas été disposé
            if (_disposed)
            {
                _logger.LogWarning("Tentative d'accès aux données sur un objet disposé");
                return Enumerable.Empty<Produit>();
            }
            
            var lines = await _fileManager.ReadAllLinesAsync();
            _logger.LogDebug("Nombre de lignes lues: {Count}", lines.Count);
            
            // Ignorer la ligne d'en-tête si elle existe
            if (lines.Count > 0 && lines[0].StartsWith("Id,"))
            {
                _logger.LogDebug("Ligne d'en-tête détectée, ignorée: {Header}", lines[0]);
                lines = lines.Skip(1).ToList();
            }
            var produits = new List<Produit>();

            for (int i = 0; i < lines.Count; i++)
            {
                var line = lines[i];
                try
                {
                    _logger.LogDebug("Parsing ligne {Index}: {Line}", i, line);
                    var produit = CsvProduitSerializer.DeserializeProduit(line);
                    produits.Add(produit);
                    _logger.LogDebug("Produit ajouté avec succès: ID={Id}, Nom={Nom}", produit.Id, produit.Nom);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Erreur lors du parsing de la ligne {Index}: {Line}", i, line);
                }
            }

            _logger.LogDebug("Nombre total de produits chargés: {Count}", produits.Count);
            return produits;
        }        /// <summary>
        /// Récupère tous les produits
        /// </summary>
        public async Task<IEnumerable<Produit>> GetAllAsync()
        {
            try
            {
                // Vérifier si l'objet n'a pas été disposé
                if (_disposed)
                {
                    _logger.LogWarning("Tentative d'accès aux données sur un objet disposé");
                    return Enumerable.Empty<Produit>();
                }
                
                await EnsureInitializedAsync();
                await _semaphore.WaitAsync();
                var produits = await GetAllInternalAsync();
                return produits;
            }
            catch (ObjectDisposedException)
            {
                _logger.LogWarning("Accès à un SemaphoreSlim disposé dans GetAllAsync");
                return Enumerable.Empty<Produit>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors de la récupération de tous les produits");
                throw;
            }
            finally
            {
                if (!_disposed)
                {
                    _semaphore.Release();
                }
            }
        }/// <summary>
        /// Récupère un produit par son ID
        /// </summary>
        public async Task<Produit?> GetByIdAsync(int id)
        {
            try
            {
                // Vérifier si l'objet n'a pas été disposé
                if (_disposed)
                {
                    _logger.LogWarning("Tentative d'accès aux données sur un objet disposé");
                    return null;
                }
                
                // _logger.LogDebug("Recherche du produit avec ID: {Id}", id);
                await _semaphore.WaitAsync();
                var produits = await GetAllInternalAsync();
                // _logger.LogDebug("Nombre de produits: {Count}", produits.Count);
                
                var result = produits.FirstOrDefault(p => p.Id == id);
                if (result != null)
                {
                    _logger.LogDebug("Produit trouvé: ID={Id}, Nom={Nom}", result.Id, result.Nom);
                }
                else
                {
                    _logger.LogWarning("Produit avec ID {Id} non trouvé", id);
                    // Log des IDs disponibles pour diagnostic
                    var availableIds = produits.Select(p => p.Id).ToList();
                    _logger.LogDebug("IDs disponibles: {Ids}", string.Join(", ", availableIds));
                }
                
                return result;
            }
            catch (ObjectDisposedException)
            {
                _logger.LogWarning("Accès à un SemaphoreSlim disposé dans GetByIdAsync");
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors de la récupération du produit avec l'ID {Id}", id);
                throw;
            }
            finally
            {
                if (!_disposed)
                {
                    _semaphore.Release();
                }
            }
        }

        /// <summary>
        /// Recherche des produits par nom
        /// </summary>
        public async Task<IEnumerable<Produit>> SearchByNameAsync(string searchTerm)
        {
            if (string.IsNullOrWhiteSpace(searchTerm))
                return Enumerable.Empty<Produit>();

            try
            {
                await _semaphore.WaitAsync();
                var produits = await GetAllInternalAsync();
                
                searchTerm = searchTerm.Trim().ToLowerInvariant();
                return produits.Where(p => (p.Nom?.ToLowerInvariant() ?? "").Contains(searchTerm));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors de la recherche de produits avec le terme {SearchTerm}", searchTerm);
                throw;
            }
            finally
            {
                _semaphore.Release();
            }
        }

        /// <summary>
        /// Récupère les produits vedettes
        /// </summary>
        public async Task<IEnumerable<Produit>> GetFeaturedAsync()
        {
            try
            {
                await _semaphore.WaitAsync();
                var produits = await GetAllInternalAsync();
                return produits.Where(p => p.Vedette);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors de la récupération des produits vedettes");
                throw;
            }
            finally
            {
                _semaphore.Release();
            }
        }

        /// <summary>
        /// Récupère les produits paginés
        /// </summary>
        public async Task<PagedResult<Produit>> GetPagedAsync(int page, int pageSize, string? searchTerm = null)
        {
            if (page < 1) page = 1;
            if (pageSize < 1) pageSize = 10;

            try
            {
                await _semaphore.WaitAsync();
                var allProduits = await GetAllInternalAsync();
                
                // Filtrer si un terme de recherche est fourni
                if (!string.IsNullOrWhiteSpace(searchTerm))
                {
                    var term = searchTerm.Trim().ToLowerInvariant();
                    allProduits = allProduits.Where(p => (p.Nom?.ToLowerInvariant() ?? "").Contains(term)).ToList();
                }
                
                var totalCount = allProduits.Count();
                
                // Appliquer la pagination
                var items = allProduits
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .ToList();
                
                return new PagedResult<Produit>
                {
                    Items = items,
                    TotalCount = totalCount,
                    Page = page,
                    PageSize = pageSize
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors de la récupération des produits paginés (page: {Page}, pageSize: {PageSize})", page, pageSize);
                throw;
            }
            finally
            {
                _semaphore.Release();
            }
        }        /// <summary>
        /// Ajoute un nouveau produit
        /// </summary>
        public async Task<OperationResult<Produit>> AddAsync(Produit produit)
        {
            if (produit == null) 
                return OperationResult<Produit>.Failure("Le produit ne peut pas être null");

            try
            {
                _logger.LogDebug("Début d'ajout du produit: {Nom}", produit.Nom);
                
                await _semaphore.WaitAsync();
                
                _logger.LogDebug("Sémaphore acquis, récupération des produits existants");
                var produits = await GetAllInternalAsync();
                
                // Validation du produit
                _logger.LogDebug("Validation du produit");
                var validationErrors = ValidateProduit(produit);
                if (validationErrors.Any())
                {
                    _logger.LogWarning("Validation échouée: {Errors}", string.Join(", ", validationErrors));
                    return OperationResult<Produit>.Failure(validationErrors);
                }
                  // Générer un nouvel ID - éviter de rappeler GetAllInternalAsync
                _logger.LogDebug("Génération du nouvel ID");
                var maxId = produits.Any() ? produits.Max(p => p.Id) : 0;
                produit.Id = maxId + 1;
                _logger.LogDebug("Nouvel ID généré: {Id}", produit.Id);
                
                // Ajouter le produit à la liste - créer une nouvelle liste
                _logger.LogDebug("Ajout du produit à la liste");
                var updatedProduits = produits.Concat(new[] { produit }).ToList();
                
                // Convertir en lignes CSV et écrire
                _logger.LogDebug("Sérialisation et écriture du fichier CSV");
                var lines = updatedProduits.Select(CsvProduitSerializer.SerializeProduit);
                await _fileManager.WriteAllLinesAsync(lines);
                
                _logger.LogInformation("Produit ajouté avec l'ID {Id}", produit.Id);
                
                return OperationResult<Produit>.Success(produit, $"Produit '{produit.Nom}' ajouté avec succès");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors de l'ajout du produit {ProductName}", produit.Nom);
                return OperationResult<Produit>.Failure($"Erreur lors de l'ajout du produit: {ex.Message}");
            }
            finally
            {
                if (!_disposed)
                {
                    _semaphore.Release();
                    _logger.LogDebug("Sémaphore libéré");
                }
            }
        }

        /// <summary>
        /// Valide un produit
        /// </summary>
        private List<string> ValidateProduit(Produit produit)
        {
            var errors = new List<string>();
            
            if (string.IsNullOrWhiteSpace(produit.Nom))
            {
                errors.Add("Le nom du produit est obligatoire");
            }
            else if (produit.Nom.Length > 100)
            {
                errors.Add("Le nom du produit ne doit pas dépasser 100 caractères");
            }
            
            if (string.IsNullOrWhiteSpace(produit.Description))
            {
                errors.Add("La description du produit est obligatoire");
            }
            else if (produit.Description.Length > 250)
            {
                errors.Add("La description du produit ne doit pas dépasser 250 caractères");
            }
            
            if (produit.Prix <= 0)
            {
                errors.Add("Le prix doit être supérieur à 0");
            }
            
            if (produit.Quantite < 0)
            {
                errors.Add("La quantité ne peut pas être négative");
            }
            else if (produit.Quantite > 150)
            {
                errors.Add("La quantité ne doit pas dépasser 150");
            }
            
            return errors;
        }

        /// <summary>
        /// Met à jour un produit existant
        /// </summary>
        public async Task<OperationResult<Produit>> UpdateAsync(Produit produit)
        {
            if (produit == null) 
                return OperationResult<Produit>.Failure("Le produit ne peut pas être null");
            
            if (produit.Id <= 0) 
                return OperationResult<Produit>.Failure("L'ID du produit doit être positif");

            try
            {
                await _semaphore.WaitAsync();
                
                var produits = await GetAllInternalAsync();
                
                // Validation du produit
                var validationErrors = ValidateProduit(produit);
                if (validationErrors.Any())
                {
                    return OperationResult<Produit>.Failure(validationErrors);
                }                // Trouver le produit à mettre à jour
                var existingProduct = produits.FirstOrDefault(p => p.Id == produit.Id);
                if (existingProduct == null)
                {
                    return OperationResult<Produit>.Failure($"Produit avec l'ID {produit.Id} non trouvé");
                }
                  // Créer une nouvelle liste avec le produit mis à jour
                var updatedProduits = produits.Select(p => p.Id == produit.Id ? produit : p).ToList();
                
                // Convertir en lignes CSV et écrire
                var lines = updatedProduits.Select(CsvProduitSerializer.SerializeProduit);
                await _fileManager.WriteAllLinesAsync(lines);
                
                _logger.LogInformation("Produit avec l'ID {Id} mis à jour", produit.Id);
                
                return OperationResult<Produit>.Success(produit, $"Produit '{produit.Nom}' mis à jour avec succès");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors de la mise à jour du produit avec l'ID {Id}", produit.Id);
                return OperationResult<Produit>.Failure($"Erreur lors de la mise à jour du produit: {ex.Message}");
            }
            finally
            {
                _semaphore.Release();
            }
        }

        /// <summary>
        /// Supprime un produit par son ID
        /// </summary>
        public async Task<OperationResult<bool>> DeleteAsync(int id)
        {
            if (id <= 0) 
                return OperationResult<bool>.Failure("L'ID du produit doit être positif");

            try
            {
                await _semaphore.WaitAsync();
                
                var produits = await GetAllInternalAsync();
                
                // Vérifier si le produit existe
                if (!produits.Any(p => p.Id == id))
                {
                    return OperationResult<bool>.Failure($"Produit avec l'ID {id} non trouvé");
                }
                  // Supprimer le produit - créer une nouvelle liste sans le produit
                var updatedProduits = produits.Where(p => p.Id != id).ToList();
                
                // Convertir en lignes CSV et écrire
                var lines = updatedProduits.Select(CsvProduitSerializer.SerializeProduit);
                await _fileManager.WriteAllLinesAsync(lines);
                
                _logger.LogInformation("Produit avec l'ID {Id} supprimé", id);
                
                return OperationResult<bool>.Success(true, $"Produit avec l'ID {id} supprimé avec succès");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors de la suppression du produit avec l'ID {Id}", id);
                return OperationResult<bool>.Failure($"Erreur lors de la suppression du produit: {ex.Message}");
            }
            finally
            {
                _semaphore.Release();
            }
        }

        /// <summary>
        /// Définit un produit comme vedette (un seul autorisé)
        /// </summary>
        public async Task<OperationResult<bool>> SetFeaturedAsync(int id)
        {
            if (id <= 0) 
                return OperationResult<bool>.Failure("L'ID du produit doit être positif");

            try
            {
                await _semaphore.WaitAsync();
                
                var produits = await GetAllInternalAsync();
                
                // Trouver le produit à mettre en vedette
                var produit = produits.FirstOrDefault(p => p.Id == id);
                if (produit == null)
                {
                    return OperationResult<bool>.Failure($"Produit avec l'ID {id} non trouvé");
                }
                
                // Réinitialiser tous les produits vedettes
                foreach (var p in produits)
                {
                    p.Vedette = false;
                }
                
                // Mettre le produit spécifié en vedette
                produit.Vedette = true;
                
                // Convertir en lignes CSV et écrire
                var lines = produits.Select(CsvProduitSerializer.SerializeProduit);
                await _fileManager.WriteAllLinesAsync(lines);
                
                _logger.LogInformation("Produit avec l'ID {Id} défini comme vedette", id);
                
                return OperationResult<bool>.Success(true, $"Produit '{produit.Nom}' défini comme vedette");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors de la définition du produit avec l'ID {Id} comme vedette", id);
                return OperationResult<bool>.Failure($"Erreur lors de la définition du produit comme vedette: {ex.Message}");
            }
            finally
            {
                _semaphore.Release();
            }
        }

        /// <summary>
        /// Supprime le statut vedette d'un produit
        /// </summary>
        public async Task<OperationResult<bool>> RemoveFeaturedAsync(int id)
        {
            if (id <= 0) 
                return OperationResult<bool>.Failure("L'ID du produit doit être positif");

            try
            {
                await _semaphore.WaitAsync();
                
                var produits = await GetAllInternalAsync();
                
                // Trouver le produit
                var produit = produits.FirstOrDefault(p => p.Id == id);
                if (produit == null)
                {
                    return OperationResult<bool>.Failure($"Produit avec l'ID {id} non trouvé");
                }
                
                // Vérifier si le produit est en vedette
                if (!produit.Vedette)
                {
                    return OperationResult<bool>.Failure($"Le produit avec l'ID {id} n'est pas en vedette");
                }
                
                // Retirer le statut vedette
                produit.Vedette = false;
                
                // Convertir en lignes CSV et écrire
                var lines = produits.Select(CsvProduitSerializer.SerializeProduit);
                await _fileManager.WriteAllLinesAsync(lines);
                
                _logger.LogInformation("Statut vedette retiré du produit avec l'ID {Id}", id);
                
                return OperationResult<bool>.Success(true, $"Statut vedette retiré du produit '{produit.Nom}'");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors du retrait du statut vedette du produit avec l'ID {Id}", id);
                return OperationResult<bool>.Failure($"Erreur lors du retrait du statut vedette: {ex.Message}");
            }
            finally
            {
                _semaphore.Release();
            }
        }

        /// <summary>
        /// Vérifie si un produit existe
        /// </summary>
        public async Task<bool> ExistsAsync(int id)
        {
            if (id <= 0) return false;

            try
            {
                await _semaphore.WaitAsync();
                var produits = await GetAllInternalAsync();
                return produits.Any(p => p.Id == id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors de la vérification de l'existence du produit avec l'ID {Id}", id);
                return false;
            }
            finally
            {
                _semaphore.Release();
            }
        }

        /// <summary>
        /// Obtient le prochain ID disponible
        /// </summary>
        public async Task<int> GetNextIdAsync()
        {
            try
            {
                await _semaphore.WaitAsync();
                var produits = await GetAllInternalAsync();
                return produits.Any() ? produits.Max(p => p.Id) + 1 : 1;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors de l'obtention du prochain ID");
                return ++_lastId; // Fallback sur l'ID en mémoire
            }
            finally
            {
                _semaphore.Release();
            }
        }

        /// <summary>
        /// Exécute une opération en lot avec transaction
        /// </summary>
        public async Task<OperationResult<bool>> ExecuteBatchAsync(Func<IProduitRepository, Task> operations)
        {
            if (operations == null)
                return OperationResult<bool>.Failure("Les opérations ne peuvent pas être null");

            try
            {
                // Démarrer une transaction
                await _transaction.BeginTransactionAsync();
                
                try
                {
                    // Exécuter les opérations
                    await operations(this);
                    
                    // Valider la transaction
                    await _transaction.CommitTransactionAsync();
                    
                    return OperationResult<bool>.Success(true, "Opérations en lot exécutées avec succès");
                }
                catch (Exception ex)
                {
                    // Annuler la transaction en cas d'erreur
                    await _transaction.RollbackTransactionAsync();
                    
                    _logger.LogError(ex, "Erreur lors de l'exécution des opérations en lot");
                    return OperationResult<bool>.Failure($"Erreur lors de l'exécution des opérations en lot: {ex.Message}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors de la gestion de la transaction");
                return OperationResult<bool>.Failure($"Erreur lors de la gestion de la transaction: {ex.Message}");
            }
        }

        /// <summary>
        /// Libère les ressources utilisées par l'instance
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Libère les ressources utilisées par l'instance
        /// </summary>
        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    _semaphore.Dispose();
                    _fileManager.Dispose();
                }

                _disposed = true;
            }
        }

        
    }
}


