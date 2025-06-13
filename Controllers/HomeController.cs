using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using CatalogueDeProduitsApp.Models;
using CatalogueDeProduitsApp.Services;

namespace CatalogueDeProduitsApp.Controllers;

public class HomeController : Controller
{
    private readonly ILogger<HomeController> _logger;
    private readonly ProduitService _produitService;

    public HomeController(ILogger<HomeController> logger, ProduitService produitService)
    {
        _logger = logger;
        _produitService = produitService;
    }    public async Task<IActionResult> Index(string search)
    {
        try
        {
            _logger.LogInformation("🔍 DÉBUT DEBUG - Chargement de la page d'accueil");
            
            var produits = await _produitService.GetAllProduitsAsync();
            
            _logger.LogInformation("🔍 DEBUG - Nombre de produits récupérés: {Count}", produits.Count);
            
            foreach (var p in produits)
            {
                _logger.LogInformation("🔍 DEBUG - Produit: ID={Id}, Nom='{Nom}', Vedette={Vedette}", p.Id, p.Nom, p.Vedette);
            }
            
            if (!string.IsNullOrEmpty(search))
            {
                produits = produits.Where(p => p.Nom != null && 
                                         p.Nom.Contains(search, StringComparison.OrdinalIgnoreCase))
                                   .ToList();
            }
            
            return View(produits);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur lors du chargement de la liste des produits: {Message}", ex.Message);
            TempData["Error"] = "Une erreur est survenue lors du chargement de la liste des produits. Veuillez réessayer.";
            return View(new List<Produit>());
        }
    }

    public IActionResult Create()
    {
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(Produit produit)
    {
        if (ModelState.IsValid)
        {
            await _produitService.CreateProduitAsync(produit);
            return RedirectToAction(nameof(Index));
        }
        return View(produit);
    }    public async Task<IActionResult> Edit(int id)
    {
        _logger.LogInformation("🔍 DÉBUT DEBUG - Édition demandée pour le produit ID: {Id}", id);
     
        
        _logger.LogInformation("Début de la requête d'édition pour le produit {Id}", id);
        
        try
        {
            // Utiliser un timeout pour la requête
            var timeoutTask = Task.Delay(TimeSpan.FromSeconds(5));
            var retrieveTask = _produitService.GetProduitByIdAsync(id);
            
            var completedTask = await Task.WhenAny(retrieveTask, timeoutTask);
            
            if (completedTask == timeoutTask)
            {
                _logger.LogWarning("Timeout lors de la récupération du produit {Id} pour édition", id);
                TempData["Error"] = "Le chargement du produit a pris trop de temps. Veuillez réessayer.";
                return RedirectToAction(nameof(Index));
            }
            
            var produit = await retrieveTask;
            
            if (produit == null)
            {
                _logger.LogWarning("Produit avec ID {Id} non trouvé pour édition", id);
                TempData["Error"] = $"Le produit avec l'ID {id} n'a pas été trouvé dans le catalogue.";
                return RedirectToAction(nameof(Index));
            }

            if (produit.Vedette)
            {
                _logger.LogInformation("Tentative d'édition d'un produit vedette (ID: {Id})", id);
                TempData["Error"] = "Le produit vedette ne peut être modifié.";
                return RedirectToAction(nameof(Index));
            }

            _logger.LogInformation("Produit avec ID {Id} trouvé et prêt pour édition", id);
            return View(produit);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur lors de la récupération du produit {Id} pour édition: {Message}", id, ex.Message);
            TempData["Error"] = "Une erreur est survenue lors de la récupération du produit. Veuillez réessayer.";
            return RedirectToAction(nameof(Index));
        }
    }[HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, Produit produit)
    {
        if (id != produit.Id)
        {
            _logger.LogWarning("ID mismatch: paramètre {ParamId} vs modèle {ModelId}", id, produit.Id);
            return NotFound();
        }

        if (ModelState.IsValid)
        {
            try
            {
                await _produitService.UpdateProduitAsync(produit);
                TempData["Success"] = "Le produit a été modifié avec succès.";
                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors de la mise à jour du produit avec l'ID {Id}", id);
                TempData["Error"] = "Une erreur est survenue lors de la mise à jour du produit. Veuillez réessayer.";
                return View(produit);
            }
        }
        
        return View(produit);
    }    public async Task<IActionResult> Delete(int id)
    {
        _logger.LogInformation("🔍 DÉBUT DEBUG - Suppression demandée pour le produit ID: {Id}", id);
       
        
        var produit = await _produitService.GetProduitByIdAsync(id);
        if (produit == null)
        {
            return NotFound();
        }

        if (produit.Vedette)
        {
            ModelState.AddModelError(string.Empty, "Le produit vedette ne peut être supprimé");
            return RedirectToAction(nameof(Index));
        }

        return View(produit);
    }    [HttpPost, ActionName("Delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(int id)
    {
        _logger.LogInformation("🔍 DÉBUT DEBUG - Suppression confirmée pour le produit ID: {Id}", id);
        
        try
        {
            var produit = await _produitService.GetProduitByIdAsync(id);
            if (produit == null)
            {
                _logger.LogWarning("Produit avec ID {Id} non trouvé pour suppression", id);
                TempData["Error"] = $"Le produit avec l'ID {id} n'a pas été trouvé.";
                return RedirectToAction(nameof(Index));
            }

            if (produit.Vedette)
            {
                _logger.LogWarning("Tentative de suppression d'un produit vedette (ID: {Id})", id);
                TempData["Error"] = "Le produit vedette ne peut être supprimé.";
                return RedirectToAction(nameof(Index));
            }

            await _produitService.DeleteProduitAsync(id);
            _logger.LogInformation("Produit avec ID {Id} supprimé avec succès", id);
            TempData["Success"] = "Le produit a été supprimé avec succès.";
            return RedirectToAction(nameof(Index));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur lors de la suppression du produit {Id}: {Message}", id, ex.Message);
            TempData["Error"] = "Une erreur est survenue lors de la suppression du produit. Veuillez réessayer.";
            return RedirectToAction(nameof(Index));
        }
    }

    // Action pour permettre l'accès direct via GET (pour les tests)
    [HttpGet("Home/DeleteConfirmed/{id:int}")]
    public IActionResult DeleteConfirmedGet(int id)
    {
        _logger.LogInformation("🔍 DEBUG - Accès GET à DeleteConfirmed pour le produit ID: {Id}", id);
        _logger.LogWarning("Tentative d'accès direct via GET à DeleteConfirmed. Redirection vers Delete.");
        
        // Rediriger vers la page de confirmation normale
        return RedirectToAction("Delete", new { id = id });
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
