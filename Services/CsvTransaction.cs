using CatalogueDeProduitsApp.Models;

namespace CatalogueDeProduitsApp.Services
{
    /// <summary>
    /// Simule un système de transactions pour les opérations CSV
    /// </summary>
    public class CsvTransaction : IDisposable
    {
        private readonly string _filePath;
        private readonly string _backupPath;
        private readonly List<ProduitOperation> _operations;
        private bool _committed = false;
        private bool _disposed = false;

        public CsvTransaction(string filePath)
        {
            _filePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
            _backupPath = _filePath + $".backup_{DateTime.Now:yyyyMMddHHmmss}_{Guid.NewGuid():N}";
            _operations = new List<ProduitOperation>();

            // Créer une sauvegarde au début de la transaction
            CreateBackup();
        }

        /// <summary>
        /// Ajoute une opération à la transaction
        /// </summary>
        public void AddOperation(OperationType type, Produit produit, Produit? originalProduit = null)
        {
            if (_committed)
                throw new InvalidOperationException("Cannot add operations to a committed transaction");

            _operations.Add(new ProduitOperation
            {
                Type = type,
                Produit = produit,
                OriginalProduit = originalProduit,
                Timestamp = DateTime.UtcNow
            });
        }        /// <summary>
        /// Démarre une nouvelle transaction (pour compatibilité avec l'interface attendue)
        /// </summary>
        public Task BeginTransactionAsync()
        {
            // La transaction est déjà initialisée dans le constructeur
            // Cette méthode est fournie pour compatibilité
            return Task.CompletedTask;
        }

        /// <summary>
        /// Valide et applique toutes les opérations de la transaction (alias pour CommitAsync)
        /// </summary>
        public async Task<TransactionResult> CommitTransactionAsync()
        {
            return await CommitAsync();
        }

        /// <summary>
        /// Annule la transaction et restaure l'état précédent (alias pour RollbackAsync)
        /// </summary>
        public async Task RollbackTransactionAsync()
        {
            await RollbackAsync();
        }

        /// <summary>
        /// Valide et applique toutes les opérations de la transaction
        /// </summary>
        public async Task<TransactionResult> CommitAsync()
        {
            if (_committed)
                throw new InvalidOperationException("Transaction already committed");

            try
            {
                // Valider toutes les opérations avant de les appliquer
                var validationResult = ValidateOperations();
                if (!validationResult.IsValid)
                {
                    return TransactionResult.Failure(validationResult.Errors);
                }

                // Charger les données actuelles
                using var fileManager = new CsvFileManager(_filePath);
                var lines = await fileManager.ReadAllLinesAsync();
                var produits = ParseProduits(lines);

                // Appliquer les opérations
                foreach (var operation in _operations)
                {
                    ApplyOperation(produits, operation);
                }

                // Valider l'état final
                var finalValidation = ValidateFinalState(produits);
                if (!finalValidation.IsValid)
                {
                    return TransactionResult.Failure(finalValidation.Errors);
                }

                // Sauvegarder les changements
                var serializedLines = produits.Select(p => CsvProduitSerializer.SerializeProduit(p));
                await fileManager.WriteAllLinesAsync(serializedLines);

                _committed = true;
                CleanupBackup();

                return TransactionResult.Success($"Transaction réussie avec {_operations.Count} opération(s)");
            }
            catch (Exception ex)
            {
                // En cas d'erreur, restaurer la sauvegarde
                await RollbackAsync();
                return TransactionResult.Failure(new[] { $"Erreur lors de la transaction: {ex.Message}" });
            }
        }

        /// <summary>
        /// Annule la transaction et restaure l'état précédent
        /// </summary>
        public async Task RollbackAsync()
        {
            if (_committed)
                return; // Rien à faire si déjà committée

            try
            {
                if (File.Exists(_backupPath))
                {
                    // Restaurer la sauvegarde
                    if (File.Exists(_filePath))
                    {
                        File.Delete(_filePath);
                    }
                    File.Copy(_backupPath, _filePath);
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Erreur lors du rollback: {ex.Message}", ex);
            }
        }

        private void CreateBackup()
        {
            if (File.Exists(_filePath))
            {
                File.Copy(_filePath, _backupPath, true);
            }
        }

        private void CleanupBackup()
        {
            try
            {
                if (File.Exists(_backupPath))
                {
                    File.Delete(_backupPath);
                }
            }
            catch
            {
                // Ignore cleanup errors
            }
        }

        private ValidationResult ValidateOperations()
        {
            var errors = new List<string>();

            foreach (var operation in _operations)
            {
                if (operation.Produit == null)
                {
                    errors.Add("Une opération contient un produit null");
                    continue;
                }

                if (!CsvProduitSerializer.ValidateProduit(operation.Produit, out var produitErrors))
                {
                    errors.AddRange(produitErrors.Select(e => $"Produit {operation.Produit.Id}: {e}"));
                }

                // Validation spécifique par type d'opération
                switch (operation.Type)
                {
                    case OperationType.Create:
                        if (operation.Produit.Id <= 0)
                            errors.Add($"ID invalide pour création: {operation.Produit.Id}");
                        break;

                    case OperationType.Update:
                        if (operation.OriginalProduit == null)
                            errors.Add($"Produit original manquant pour mise à jour de {operation.Produit.Id}");
                        break;

                    case OperationType.Delete:
                        if (operation.Produit.Id <= 0)
                            errors.Add($"ID invalide pour suppression: {operation.Produit.Id}");
                        break;
                }
            }

            return new ValidationResult { IsValid = errors.Count == 0, Errors = errors };
        }

        private ValidationResult ValidateFinalState(List<Produit> produits)
        {
            var errors = new List<string>();

            // Vérifier l'unicité des IDs
            var duplicateIds = produits.GroupBy(p => p.Id)
                                      .Where(g => g.Count() > 1)
                                      .Select(g => g.Key);

            foreach (var id in duplicateIds)
            {
                errors.Add($"ID dupliqué détecté: {id}");
            }

            // Vérifier qu'il n'y a qu'un seul produit vedette
            var produitVedettes = produits.Where(p => p.Vedette).ToList();
            if (produitVedettes.Count > 1)
            {
                errors.Add($"Plusieurs produits vedettes détectés: {string.Join(", ", produitVedettes.Select(p => p.Id))}");
            }

            return new ValidationResult { IsValid = errors.Count == 0, Errors = errors };
        }

        private List<Produit> ParseProduits(List<string> lines)
        {
            var produits = new List<Produit>();
            for (int i = 0; i < lines.Count; i++)
            {
                try
                {
                    var produit = CsvProduitSerializer.DeserializeProduit(lines[i], i + 1);
                    produits.Add(produit);
                }
                catch (Exception ex)
                {
                    throw new InvalidDataException($"Erreur ligne {i + 1}: {ex.Message}", ex);
                }
            }
            return produits;
        }

        private void ApplyOperation(List<Produit> produits, ProduitOperation operation)
        {
            switch (operation.Type)
            {
                case OperationType.Create:
                    produits.Add(operation.Produit!);
                    break;

                case OperationType.Update:
                    var existingIndex = produits.FindIndex(p => p.Id == operation.Produit!.Id);
                    if (existingIndex >= 0)
                    {
                        produits[existingIndex] = operation.Produit!;
                    }
                    break;

                case OperationType.Delete:
                    produits.RemoveAll(p => p.Id == operation.Produit!.Id);
                    break;
            }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                if (!_committed)
                {
                    // Rollback automatique si pas committée
                    Task.Run(async () => await RollbackAsync()).Wait();
                }
                else
                {
                    CleanupBackup();
                }
                _disposed = true;
            }
        }
    }

    // Classes utilitaires
    public class ProduitOperation
    {
        public OperationType Type { get; set; }
        public Produit? Produit { get; set; }
        public Produit? OriginalProduit { get; set; }
        public DateTime Timestamp { get; set; }
    }

    public enum OperationType
    {
        Create,
        Update,
        Delete
    }

    public class ValidationResult
    {
        public bool IsValid { get; set; }
        public List<string> Errors { get; set; } = new();
    }

    public class TransactionResult
    {
        public bool IsSuccess { get; set; }
        public string Message { get; set; } = string.Empty;
        public List<string> Errors { get; set; } = new();

        public static TransactionResult Success(string message)
        {
            return new TransactionResult { IsSuccess = true, Message = message };
        }

        public static TransactionResult Failure(IEnumerable<string> errors)
        {
            return new TransactionResult 
            { 
                IsSuccess = false, 
                Errors = errors.ToList(),
                Message = "Transaction échouée"
            };
        }
    }
}
