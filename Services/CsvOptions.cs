namespace CatalogueDeProduitsApp.Services
{
    /// <summary>
    /// Options de configuration pour les services CSV
    /// </summary>
    public class CsvOptions
    {
        public const string SectionName = "Csv";

        /// <summary>
        /// Chemin vers le fichier CSV
        /// </summary>
        public string FilePath { get; set; } = "Data/produits.csv";

        /// <summary>
        /// Durée d'expiration du cache en minutes
        /// </summary>
        public int CacheExpiryMinutes { get; set; } = 5;

        /// <summary>
        /// Activer les sauvegardes automatiques
        /// </summary>
        public bool BackupEnabled { get; set; } = true;

        /// <summary>
        /// Nombre de jours de rétention des sauvegardes
        /// </summary>
        public int BackupRetentionDays { get; set; } = 7;

        /// <summary>
        /// Nombre maximum de tentatives en cas d'erreur
        /// </summary>
        public int MaxRetryAttempts { get; set; } = 3;

        /// <summary>
        /// Délai entre les tentatives en millisecondes
        /// </summary>
        public int RetryDelayMs { get; set; } = 100;

        /// <summary>
        /// Taille maximale du fichier en MB
        /// </summary>
        public int MaxFileSizeMB { get; set; } = 10;

        /// <summary>
        /// Activer la compression des sauvegardes
        /// </summary>
        public bool CompressBackups { get; set; } = true;

        /// <summary>
        /// Séparateur CSV (par défaut point-virgule)
        /// </summary>
        public string CsvSeparator { get; set; } = ",";

        /// <summary>
        /// Encodage du fichier
        /// </summary>
        public string Encoding { get; set; } = "UTF-8";
    }
}
