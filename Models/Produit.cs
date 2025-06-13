using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;

namespace CatalogueDeProduitsApp.Models
{
    public class Produit
    {
        public int Id { get; set; }

        [Required(ErrorMessage = "Le nom est obligatoire")]
        [StringLength(100, ErrorMessage = "Le nom ne doit pas dépasser 100 caractères")]
        [RegularExpression(@"^[a-zA-ZÀ-ÿ\s-]+$", ErrorMessage = "Le nom ne doit contenir que des lettres")]
        [Display(Name = "Nom")]
        public string? Nom { get; set; }

        [Required(ErrorMessage = "La description est obligatoire")]
        [StringLength(250, ErrorMessage = "La description ne doit pas dépasser 250 caractères")]
        [Display(Name = "Description")]
        public string? Description { get; set; }

        [Required(ErrorMessage = "Le prix est obligatoire")]
        [Range(0.01, double.MaxValue, ErrorMessage = "Le prix doit être supérieur à 0")]
        [RegularExpression(@"^\d+(\.\d{1,2})?$", ErrorMessage = "Le prix doit avoir au maximum 2 décimales")]
        [Display(Name = "Prix")]
        public decimal Prix { get; set; }

        [Required(ErrorMessage = "La quantité est obligatoire")]
        [Range(0, 150, ErrorMessage = "La quantité doit être comprise entre 0 et 150")]
        [Display(Name = "Quantité")]
        public int Quantite { get; set; }

        [Required(ErrorMessage = "L'image est obligatoire")]
        [RegularExpression(@"^.+\.(jpg|png)$", ErrorMessage = "L'image doit avoir l'extension .jpg ou .png")]
        [Display(Name = "Image")]
        public string? Image { get; set; }

        [Display(Name = "Est produit vedette")]
        public bool Vedette { get; set; } = false;
    }
}
