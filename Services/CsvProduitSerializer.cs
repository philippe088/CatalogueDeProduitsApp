using CatalogueDeProduitsApp.Models;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace CatalogueDeProduitsApp.Services
{    /// <summary>
    /// Sérialiseur CSV robuste avec gestion d'erreurs et validation
    /// </summary>
    public class CsvProduitSerializer
    {
        private const string CSV_SEPARATOR = ","; // Virgule pour correspondre au fichier produits.csv
        private const string QUOTE_CHAR = "\"";
        private const string ESCAPED_QUOTE = "\"\"";

        /// <summary>
        /// Convertit un produit en ligne CSV avec échappement proper
        /// </summary>
        public static string SerializeProduit(Produit produit)
        {
            if (produit == null)
                throw new ArgumentNullException(nameof(produit));

            var fields = new string[]
            {
                produit.Id.ToString(CultureInfo.InvariantCulture),
                EscapeCsvField(produit.Nom ?? string.Empty),
                EscapeCsvField(produit.Description ?? string.Empty),
                produit.Prix.ToString("F2", CultureInfo.InvariantCulture),
                produit.Quantite.ToString(CultureInfo.InvariantCulture),
                EscapeCsvField(produit.Image ?? string.Empty),
                produit.Vedette.ToString().ToLowerInvariant()
            };

            return string.Join(CSV_SEPARATOR, fields);
        }

        /// <summary>
        /// Convertit une ligne CSV en produit avec validation
        /// </summary>
        public static Produit DeserializeProduit(string csvLine, int lineNumber = 0)
        {
            if (string.IsNullOrWhiteSpace(csvLine))
                throw new ArgumentException("La ligne CSV ne peut pas être vide", nameof(csvLine));

            try
            {
                var fields = ParseCsvLine(csvLine);
                
                if (fields.Length != 7)
                {
                    throw new FormatException($"Nombre de champs incorrect. Attendu: 7, Trouvé: {fields.Length}");
                }

                var produit = new Produit();

                // Validation et parsing de chaque champ
                if (!int.TryParse(fields[0], out int id) || id <= 0)
                {
                    throw new FormatException($"ID invalide: '{fields[0]}'. Doit être un entier positif.");
                }
                produit.Id = id;

                produit.Nom = UnescapeCsvField(fields[1]);
                if (string.IsNullOrWhiteSpace(produit.Nom))
                {
                    throw new FormatException("Le nom du produit ne peut pas être vide");
                }

                produit.Description = UnescapeCsvField(fields[2]);
                if (string.IsNullOrWhiteSpace(produit.Description))
                {
                    throw new FormatException("La description du produit ne peut pas être vide");
                }

                if (!decimal.TryParse(fields[3], NumberStyles.Number, CultureInfo.InvariantCulture, out decimal prix) || prix < 0)
                {
                    throw new FormatException($"Prix invalide: '{fields[3]}'. Doit être un nombre décimal positif.");
                }
                produit.Prix = prix;

                if (!int.TryParse(fields[4], out int quantite) || quantite < 0)
                {
                    throw new FormatException($"Quantité invalide: '{fields[4]}'. Doit être un entier positif ou zéro.");
                }
                produit.Quantite = quantite;                produit.Image = UnescapeCsvField(fields[5]);

                // Parsing plus flexible pour les valeurs booléennes
                var vedetteValue = fields[6].Trim().ToLowerInvariant();
                if (vedetteValue == "true" || vedetteValue == "1" || vedetteValue == "yes" || vedetteValue == "oui")
                {
                    produit.Vedette = true;
                }
                else if (vedetteValue == "false" || vedetteValue == "0" || vedetteValue == "no" || vedetteValue == "non")
                {
                    produit.Vedette = false;
                }
                else
                {
                    throw new FormatException($"Valeur Vedette invalide: '{fields[6]}'. Doit être 'true', 'false', '1', '0', etc.");
                }

                return produit;
            }
            catch (Exception ex)
            {
                throw new InvalidDataException($"Erreur lors du parsing de la ligne {lineNumber}: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Parse une ligne CSV en gérant les guillemets et échappements
        /// </summary>
        private static string[] ParseCsvLine(string line)
        {
            var fields = new List<string>();
            var currentField = new StringBuilder();
            bool inQuotes = false;
            
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                
                if (c == '"')
                {
                    if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                    {
                        // Guillemet échappé
                        currentField.Append('"');
                        i++; // Skip next quote
                    }
                    else
                    {
                        // Toggle quote mode
                        inQuotes = !inQuotes;
                    }
                }
                else if (c == ',' && !inQuotes)
                {
                    // Fin de champ
                    fields.Add(currentField.ToString());
                    currentField.Clear();
                }
                else
                {
                    currentField.Append(c);
                }
            }
            
            // Ajouter le dernier champ
            fields.Add(currentField.ToString());
            
            return fields.ToArray();
        }

        /// <summary>
        /// Échappe un champ CSV (ajoute des guillemets si nécessaire)
        /// </summary>
        private static string EscapeCsvField(string field)
        {
            if (string.IsNullOrEmpty(field))
                return string.Empty;

            // Échapper si le champ contient des caractères spéciaux
            if (field.Contains(CSV_SEPARATOR) || field.Contains(QUOTE_CHAR) || field.Contains('\n') || field.Contains('\r'))
            {
                var escaped = field.Replace(QUOTE_CHAR, ESCAPED_QUOTE);
                return $"{QUOTE_CHAR}{escaped}{QUOTE_CHAR}";
            }

            return field;
        }

        /// <summary>
        /// Supprime l'échappement d'un champ CSV
        /// </summary>
        private static string UnescapeCsvField(string field)
        {
            if (string.IsNullOrEmpty(field))
                return string.Empty;

            // Supprimer les guillemets de début et fin si présents
            if (field.StartsWith(QUOTE_CHAR) && field.EndsWith(QUOTE_CHAR) && field.Length >= 2)
            {
                field = field.Substring(1, field.Length - 2);
                // Remplacer les guillemets échappés
                field = field.Replace(ESCAPED_QUOTE, QUOTE_CHAR);
            }

            return field;
        }

        /// <summary>
        /// Valide l'intégrité d'un produit
        /// </summary>
        public static bool ValidateProduit(Produit produit, out List<string> errors)
        {
            errors = new List<string>();

            if (produit == null)
            {
                errors.Add("Le produit ne peut pas être null");
                return false;
            }

            if (produit.Id <= 0)
                errors.Add("L'ID doit être supérieur à 0");

            if (string.IsNullOrWhiteSpace(produit.Nom))
                errors.Add("Le nom est obligatoire");
            else if (produit.Nom.Length > 100)
                errors.Add("Le nom ne doit pas dépasser 100 caractères");

            if (string.IsNullOrWhiteSpace(produit.Description))
                errors.Add("La description est obligatoire");
            else if (produit.Description.Length > 250)
                errors.Add("La description ne doit pas dépasser 250 caractères");

            if (produit.Prix < 0)
                errors.Add("Le prix ne peut pas être négatif");

            if (produit.Quantite < 0)
                errors.Add("La quantité ne peut pas être négative");

            return errors.Count == 0;
        }
    }
}
