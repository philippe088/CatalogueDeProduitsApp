using Microsoft.AspNetCore.Mvc;
using CatalogueDeProduitsApp.Models;
using CatalogueDeProduitsApp.Services;
using CatalogueDeProduitsApp.Services.Interfaces;

namespace CatalogueDeProduitsApp.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Produces("application/json")]
    public class ProduitsController : ControllerBase
    {
        private readonly IProduitRepository _produitRepository;
        private readonly ProduitService _produitService;
        private readonly CsvMonitoringService _monitoringService;
        private readonly ILogger<ProduitsController> _logger;

        public ProduitsController(
            IProduitRepository produitRepository,
            ProduitService produitService,
            CsvMonitoringService monitoringService,
            ILogger<ProduitsController> logger)
        {
            _produitRepository = produitRepository;
            _produitService = produitService;
            _monitoringService = monitoringService;
            _logger = logger;
        }

        /// <summary>
        /// Récupère tous les produits
        /// </summary>
        [HttpGet]
        public async Task<ActionResult<IEnumerable<Produit>>> GetProduits()
        {
            const string operationName = "GetAllProduits";
            
            try
            {
                _monitoringService.StartOperation(operationName);
                var produits = await _produitRepository.GetAllAsync();
                _monitoringService.EndOperation(operationName, success: true);
                
                _logger.LogInformation("Récupération de {Count} produits", produits.Count());
                return Ok(produits);
            }
            catch (Exception ex)
            {
                _monitoringService.EndOperation(operationName, success: false, ex.Message);
                _logger.LogError(ex, "Erreur lors de la récupération des produits");
                return StatusCode(500, "Erreur interne du serveur");
            }
        }

        /// <summary>
        /// Récupère un produit par son ID
        /// </summary>
        [HttpGet("{id}")]
        public async Task<ActionResult<Produit>> GetProduit(int id)
        {
            const string operationName = "GetProduitById";
            try
            {
                _monitoringService.StartOperation(operationName);
                var produit = await _produitRepository.GetByIdAsync(id);
                _monitoringService.EndOperation(operationName);

                if (produit == null)
                {
                    _logger.LogWarning("Produit avec ID {Id} non trouvé", id);
                    return NotFound($"Produit avec l'ID {id} non trouvé");
                }

                return Ok(produit);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors de la récupération du produit {Id}", id);
                return StatusCode(500, "Erreur interne du serveur");
            }
        }

        /// <summary>
        /// Crée un nouveau produit
        /// </summary>
        [HttpPost]
        public async Task<ActionResult<Produit>> CreateProduit([FromBody] Produit produit)
        {
            const string operationName = "CreateProduit";
            
            try
            {
                if (produit == null)
                {
                    return BadRequest("Les données du produit sont requises");
                }

                if (!ModelState.IsValid)
                {
                    return BadRequest(ModelState);
                }

                _monitoringService.StartOperation(operationName);
                var nouveauProduit = await _produitRepository.AddAsync(produit);
                _monitoringService.EndOperation(operationName, success: true);

                if (nouveauProduit?.Data != null)
                {
                    _logger.LogInformation("Nouveau produit créé avec ID {Id}", nouveauProduit.Data.Id);
                    return CreatedAtAction(
                        nameof(GetProduit), 
                        new { id = nouveauProduit.Data.Id }, 
                        nouveauProduit.Data);
                }
                else
                {
                    _logger.LogError("Erreur lors de la création du produit - résultat null");
                    return StatusCode(500, "Erreur lors de la création du produit");
                }
            }
            catch (Exception ex)
            {
                _monitoringService.EndOperation(operationName, success: false, ex.Message);
                _logger.LogError(ex, "Erreur lors de la création du produit");
                return StatusCode(500, "Erreur interne du serveur");
            }
        }

        /// <summary>
        /// Met à jour un produit existant
        /// </summary>
        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateProduit(int id, [FromBody] Produit produit)
        {
            try
            {
                if (produit == null)
                {
                    return BadRequest("Les données du produit sont requises");
                }

                if (id != produit.Id)
                {
                    return BadRequest("L'ID dans l'URL ne correspond pas à l'ID du produit");
                }

                if (!ModelState.IsValid)
                {
                    return BadRequest(ModelState);
                }

                _monitoringService.StartOperation("UpdateProduit");
                var produitExistant = await _produitRepository.GetByIdAsync(id);
                
                if (produitExistant == null)
                {
                    _monitoringService.EndOperation("UpdateProduit");
                    return NotFound($"Produit avec l'ID {id} non trouvé");
                }

                await _produitRepository.UpdateAsync(produit);
                _monitoringService.EndOperation("UpdateProduit");

                _logger.LogInformation("Produit avec ID {Id} mis à jour", id);
                return NoContent();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors de la mise à jour du produit {Id}", id);
                return StatusCode(500, "Erreur interne du serveur");
            }
        }

        /// <summary>
        /// Supprime un produit
        /// </summary>
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteProduit(int id)
        {
            try
            {
                _monitoringService.StartOperation("DeleteProduit");
                var produit = await _produitRepository.GetByIdAsync(id);
                
                if (produit == null)
                {
                    _monitoringService.EndOperation("DeleteProduit");
                    return NotFound($"Produit avec l'ID {id} non trouvé");
                }

                await _produitRepository.DeleteAsync(id);
                _monitoringService.EndOperation("DeleteProduit");

                _logger.LogInformation("Produit avec ID {Id} supprimé", id);
                return NoContent();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors de la suppression du produit {Id}", id);
                return StatusCode(500, "Erreur interne du serveur");
            }
        }

        /// <summary>
        /// Recherche des produits par nom
        /// </summary>
        [HttpGet("search")]
        public async Task<ActionResult<IEnumerable<Produit>>> SearchProduits([FromQuery] string nom)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(nom))
                {
                    return BadRequest("Le paramètre de recherche 'nom' est requis");
                }

                _monitoringService.StartOperation("SearchProduits");
                var produits = await _produitRepository.GetAllAsync();
                var produitsFilters = produits.Where(p => 
                    p.Nom != null && p.Nom.Contains(nom, StringComparison.OrdinalIgnoreCase));
                _monitoringService.EndOperation("SearchProduits");

                _logger.LogInformation("Recherche effectuée pour '{Nom}', {Count} résultats", nom, produitsFilters.Count());
                return Ok(produitsFilters);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors de la recherche de produits");
                return StatusCode(500, "Erreur interne du serveur");
            }
        }

        /// <summary>
        /// Récupère les statistiques des produits
        /// </summary>
        [HttpGet("stats")]
        public async Task<ActionResult<object>> GetStats()
        {
            try
            {
                _monitoringService.StartOperation("GetProduitsStats");
                var produits = await _produitRepository.GetAllAsync();
                
                var stats = new
                {
                    TotalProduits = produits.Count(),
                    PrixMoyen = produits.Any() ? produits.Average(p => p.Prix) : 0,
                    PrixMin = produits.Any() ? produits.Min(p => p.Prix) : 0,
                    PrixMax = produits.Any() ? produits.Max(p => p.Prix) : 0,
                };
                
                _monitoringService.EndOperation("GetProduitsStats");
                return Ok(stats);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors du calcul des statistiques");
                return StatusCode(500, "Erreur interne du serveur");
            }
        }
    }
}
