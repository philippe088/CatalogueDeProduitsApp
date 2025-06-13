using CatalogueDeProduitsApp.Models;

namespace CatalogueDeProduitsApp.Services.Interfaces
{
    /// <summary>
    /// Interface pour le repository des produits
    /// </summary>
    public interface IProduitRepository
    {
        /// <summary>
        /// Récupère tous les produits
        /// </summary>
        Task<IEnumerable<Produit>> GetAllAsync();

        /// <summary>
        /// Récupère un produit par son ID
        /// </summary>
        Task<Produit?> GetByIdAsync(int id);

        /// <summary>
        /// Recherche des produits par nom
        /// </summary>
        Task<IEnumerable<Produit>> SearchByNameAsync(string searchTerm);

        /// <summary>
        /// Récupère les produits vedettes
        /// </summary>
        Task<IEnumerable<Produit>> GetFeaturedAsync();

        /// <summary>
        /// Récupère les produits paginés
        /// </summary>
        Task<PagedResult<Produit>> GetPagedAsync(int page, int pageSize, string? searchTerm = null);

        /// <summary>
        /// Ajoute un nouveau produit
        /// </summary>
        Task<OperationResult<Produit>> AddAsync(Produit produit);

        /// <summary>
        /// Met à jour un produit existant
        /// </summary>
        Task<OperationResult<Produit>> UpdateAsync(Produit produit);

        /// <summary>
        /// Supprime un produit
        /// </summary>
        Task<OperationResult<bool>> DeleteAsync(int id);

        /// <summary>
        /// Définit un produit comme vedette (un seul autorisé)
        /// </summary>
        Task<OperationResult<bool>> SetFeaturedAsync(int id);

        /// <summary>
        /// Supprime le statut vedette d'un produit
        /// </summary>
        Task<OperationResult<bool>> RemoveFeaturedAsync(int id);

        /// <summary>
        /// Vérifie si un produit existe
        /// </summary>
        Task<bool> ExistsAsync(int id);

        /// <summary>
        /// Obtient le prochain ID disponible
        /// </summary>
        Task<int> GetNextIdAsync();

        /// <summary>
        /// Exécute une opération en lot avec transaction
        /// </summary>
        Task<OperationResult<bool>> ExecuteBatchAsync(Func<IProduitRepository, Task> operations);
    }

    /// <summary>
    /// Résultat d'une opération avec gestion d'erreurs
    /// </summary>
    public class OperationResult<T>
    {
        public bool IsSuccess { get; set; }
        public T? Data { get; set; }
        public string Message { get; set; } = string.Empty;
        public List<string> Errors { get; set; } = new();

        public static OperationResult<T> Success(T data, string message = "")
        {
            return new OperationResult<T>
            {
                IsSuccess = true,
                Data = data,
                Message = message
            };
        }

        public static OperationResult<T> Failure(string error)
        {
            return new OperationResult<T>
            {
                IsSuccess = false,
                Errors = new List<string> { error }
            };
        }

        public static OperationResult<T> Failure(IEnumerable<string> errors)
        {
            return new OperationResult<T>
            {
                IsSuccess = false,
                Errors = errors.ToList()
            };
        }
    }

    /// <summary>
    /// Résultat paginé
    /// </summary>
    public class PagedResult<T>
    {
        public IEnumerable<T> Items { get; set; } = Enumerable.Empty<T>();
        public int TotalCount { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }
        public int TotalPages => (int)Math.Ceiling((double)TotalCount / PageSize);
        public bool HasNextPage => Page < TotalPages;
        public bool HasPreviousPage => Page > 1;
    }
}
