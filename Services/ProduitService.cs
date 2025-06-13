using CatalogueDeProduitsApp.Models;
using CatalogueDeProduitsApp.Services.Interfaces;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace CatalogueDeProduitsApp.Services
{
    /// <summary>
    /// Service de gestion des produits avec logique métier
    /// </summary>
    public class ProduitService
    {
        private readonly IProduitRepository _produitRepository;
        private readonly ILogger<ProduitService> _logger;

        public ProduitService(IProduitRepository produitRepository, ILogger<ProduitService> logger)
        {
            _produitRepository = produitRepository;
            _logger = logger;
        }

        public ProduitService(IWebHostEnvironment environment, IProduitRepository produitRepository, ILogger<ProduitService> logger)
        {
            _produitRepository = produitRepository;
            _logger = logger;
        }

    /// <summary>
    /// Récupère tous les produits
    /// </summary>
    public async Task<List<Produit>> GetAllProduitsAsync()
    {
        try
        {
            var result = await _produitRepository.GetAllAsync();
            return result.ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur lors de la récupération de tous les produits: {Message}", ex.Message);
            // Au lieu de retourner une liste vide, nous pouvons tenter une approche de récupération
            try
            {
                // Donner un peu de temps au système de fichiers
                await Task.Delay(500);
                var secondAttempt = await _produitRepository.GetAllAsync();
                _logger.LogInformation("Récupération réussie après une seconde tentative");
                return secondAttempt.ToList();
            }
            catch (Exception retryEx)
            {
                _logger.LogError(retryEx, "Échec de la seconde tentative de récupération des produits: {Message}", retryEx.Message);
                return new List<Produit>();
            }
        }
    }    /// <summary>
    /// Récupère un produit par son ID
    /// </summary>
    public async Task<Produit?> GetProduitByIdAsync(int id)
    {
        try
        {
            _logger.LogInformation("Tentative de récupération du produit avec ID: {Id}", id);
            
            // Tentative directe avec timeout
            var timeoutTask = Task.Delay(TimeSpan.FromSeconds(5));
            var retrieveTask = _produitRepository.GetByIdAsync(id);
            
            var completedTask = await Task.WhenAny(retrieveTask, timeoutTask);
            
            if (completedTask == timeoutTask)
            {
                _logger.LogWarning("Timeout lors de la récupération du produit {Id}", id);
                return CreatePlaceholderProduct(id);
            }
            
            var produit = await retrieveTask;
            if (produit != null)
            {
                _logger.LogInformation("Produit trouvé avec succès: ID={Id}, Nom={Nom}", produit.Id, produit.Nom);
                return produit;
            }
            
            _logger.LogWarning("Produit {Id} non trouvé, tentative de récupération alternative", id);
            
            // Tentative alternative : récupérer tous les produits et filtrer
            try 
            {
                var allProducts = await _produitRepository.GetAllAsync();
                var alternativeResult = allProducts.FirstOrDefault(p => p.Id == id);
                
                if (alternativeResult != null)
                {
                    _logger.LogInformation("Produit trouvé via méthode alternative: ID={Id}, Nom={Nom}", alternativeResult.Id, alternativeResult.Nom);
                    return alternativeResult;
                }
                
                _logger.LogWarning("Produit {Id} non trouvé même avec la méthode alternative. Produits disponibles: {Count}", id, allProducts.Count());
                var availableIds = string.Join(", ", allProducts.Select(p => p.Id));
                _logger.LogInformation("IDs disponibles: {Ids}", availableIds);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors de la récupération alternative du produit {Id}", id);
            }
            
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur lors de la récupération du produit avec l'ID {Id}: {Message}", id, ex.Message);
            return null;
        }
    }
    
    /// <summary>
    /// Crée un produit temporaire en cas d'échec de récupération
    /// </summary>
    private Produit CreatePlaceholderProduct(int id)
    {
        _logger.LogWarning("Création d'un produit temporaire pour l'ID {Id}", id);
        return new Produit
        {
            Id = id,
            Nom = "[Produit temporaire]",
            Description = "Ce produit n'a pas pu être chargé correctement. Veuillez réessayer plus tard.",
            Prix = 0,
            Quantite = 0,
            Image = "placeholder.jpg",
            Vedette = false
        };
    }        /// <summary>
        /// Crée un nouveau produit
        /// </summary>
        public async Task<bool> CreateProduitAsync(Produit produit)
        {
            try
            {
                _logger.LogDebug("Début de création du produit: {Nom}", produit.Nom);
                
                // Logique métier : Si le produit est vedette, désactiver les autres produits vedettes
                if (produit.Vedette)
                {
                    _logger.LogDebug("Le produit est vedette, désactivation des autres produits vedettes");
                    await MakeOtherProductsNonFeaturedAsync();
                }
                
                _logger.LogDebug("Ajout du produit au repository");
                
                // Utiliser le repository pour ajouter le produit
                var result = await _produitRepository.AddAsync(produit);
                
                _logger.LogDebug("Résultat de l'ajout: {IsSuccess}", result.IsSuccess);
                
                return result.IsSuccess;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors de la création du produit {NomProduit}", produit.Nom);
                return false;
            }
        }

        /// <summary>
        /// Met à jour un produit existant
        /// </summary>
        public async Task<bool> UpdateProduitAsync(Produit produit)
        {
            try
            {
                var existingProduit = await _produitRepository.GetByIdAsync(produit.Id);
                if (existingProduit == null)
                {
                    _logger.LogWarning("Tentative de mise à jour d'un produit inexistant : {Id}", produit.Id);
                    return false;
                }

                // Logique métier : Si le produit devient vedette, désactiver les autres produits vedettes
                if (produit.Vedette && !existingProduit.Vedette)
                {
                    await MakeOtherProductsNonFeaturedAsync(produit.Id);
                }

                // Utiliser le repository pour mettre à jour le produit
                var result = await _produitRepository.UpdateAsync(produit);
                return result.IsSuccess;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors de la mise à jour du produit {Id}", produit.Id);
                return false;
            }
        }

        /// <summary>
        /// Supprime un produit par son ID
        /// </summary>
        public async Task<bool> DeleteProduitAsync(int id)
        {
            try
            {
                var result = await _produitRepository.DeleteAsync(id);
                return result.IsSuccess;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors de la suppression du produit {Id}", id);
                return false;
            }
        }

        /// <summary>
        /// Définit un produit comme vedette et désactive les autres
        /// </summary>
        public async Task<bool> SetFeaturedAsync(int id)
        {
            try
            {
                // Vérifier que le produit existe
                if (!await _produitRepository.ExistsAsync(id))
                {
                    _logger.LogWarning("Tentative de définir comme vedette un produit inexistant : {Id}", id);
                    return false;
                }

                // Désactiver tous les autres produits vedettes
                await MakeOtherProductsNonFeaturedAsync(id);

                // Définir ce produit comme vedette
                var result = await _produitRepository.SetFeaturedAsync(id);
                return result.IsSuccess;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors de la définition du produit {Id} comme vedette", id);
                return false;
            }
        }

        /// <summary>
        /// Désactive le statut vedette d'un produit
        /// </summary>
        public async Task<bool> RemoveFeaturedAsync(int id)
        {
            try
            {
                var result = await _produitRepository.RemoveFeaturedAsync(id);
                return result.IsSuccess;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors de la désactivation du statut vedette du produit {Id}", id);
                return false;
            }
        }

        /// <summary>
        /// Recherche des produits par nom
        /// </summary>
        public async Task<List<Produit>> SearchProduitsAsync(string searchTerm)
        {
            try
            {
                var result = await _produitRepository.SearchByNameAsync(searchTerm);
                return result.ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors de la recherche de produits avec le terme {SearchTerm}", searchTerm);
                return new List<Produit>();
            }
        }

        /// <summary>
        /// Récupère les produits paginés
        /// </summary>
        public async Task<PagedResult<Produit>> GetPagedProduitsAsync(int page, int pageSize, string? searchTerm = null)
        {
            try
            {
                return await _produitRepository.GetPagedAsync(page, pageSize, searchTerm);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors de la récupération des produits paginés");
                return new PagedResult<Produit>
                {
                    Items = new List<Produit>(),
                    Page = page,
                    PageSize = pageSize,
                    TotalCount = 0
                };
            }
        }        /// <summary>
        /// Désactive le statut vedette de tous les produits sauf celui spécifié
        /// </summary>
        private async Task MakeOtherProductsNonFeaturedAsync(int? excludedProductId = null)
        {
            try
            {
                _logger.LogDebug("Début de désactivation des produits vedettes, excluant l'ID: {ExcludedId}", excludedProductId);
                
                var produits = await _produitRepository.GetAllAsync();
                var produitsToUpdate = produits.Where(p => p.Vedette && (excludedProductId == null || p.Id != excludedProductId)).ToList();
                
                _logger.LogDebug("Nombre de produits à mettre à jour: {Count}", produitsToUpdate.Count);
                
                if (!produitsToUpdate.Any())
                {
                    _logger.LogDebug("Aucun produit vedette à désactiver");
                    return;
                }
                
                // Éviter la récursion en utilisant directement le repository sans passer par ExecuteBatchAsync
                foreach (var produit in produitsToUpdate)
                {
                    _logger.LogDebug("Désactivation du produit vedette: ID={Id}, Nom={Nom}", produit.Id, produit.Nom);
                    produit.Vedette = false;
                    var result = await _produitRepository.UpdateAsync(produit);                    if (!result.IsSuccess)
                    {
                        var errorMessage = result.Errors.Any() ? string.Join(", ", result.Errors) : "Erreur inconnue";
                        _logger.LogWarning("Échec de la mise à jour du produit {Id}: {Error}", produit.Id, errorMessage);
                    }
                }
                
                _logger.LogDebug("Fin de désactivation des produits vedettes");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors de la désactivation du statut vedette des autres produits");
                throw; // Remonter l'exception pour qu'elle soit gérée par la méthode appelante
            }
        }
    }
}
