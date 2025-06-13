# Explication du code CsvFileManager

Cette classe gère la lecture et l'écriture de fichiers CSV de manière thread-safe avec une gestion avancée de la concurrence.

## Directives Using et Namespace
```csharp
using System.Collections.Concurrent;
using System.Text;

namespace CatalogueDeProduitsApp.Services
```
- Importation de `System.Collections.Concurrent` pour utiliser des collections thread-safe
- Importation de `System.Text` pour manipuler l'encodage des fichiers
- Définition du namespace `CatalogueDeProduitsApp.Services` où se trouve la classe

## Déclaration de la classe
```csharp
public class CsvFileManager : IDisposable
```
- Classe publique implémentant `IDisposable` pour libérer proprement les ressources

## Variables membres
```csharp
private readonly string _filePath;
private readonly SemaphoreSlim _semaphore;
private readonly object _lockObject = new object();
private static readonly ConcurrentDictionary<string, SemaphoreSlim> _fileLocks = new();
```
- `_filePath` : Chemin du fichier CSV à gérer
- `_semaphore` : Sémaphore pour synchroniser l'accès au fichier
- `_lockObject` : Objet de verrouillage (non utilisé dans le code actuel)
- `_fileLocks` : Dictionnaire concurrent statique qui maintient un sémaphore pour chaque fichier (partagé entre toutes les instances)

## Constructeur
```csharp
public CsvFileManager(string filePath)
{
    _filePath = filePath;
    // Un semaphore par fichier pour éviter les accès concurrents
    _semaphore = _fileLocks.GetOrAdd(_filePath, _ => new SemaphoreSlim(1, 1));
}
```
- Initialise le chemin du fichier
- Récupère ou crée un sémaphore pour ce fichier spécifique dans le dictionnaire concurrent
- Le sémaphore permet une seule entrée et une file d'attente maximale de 1

## Méthode ReadAllLinesAsync
```csharp
public async Task<List<string>> ReadAllLinesAsync()
{
    await _semaphore.WaitAsync();
    try
    {
```
- Méthode asynchrone pour lire le fichier
- Attend d'acquérir le sémaphore (bloque jusqu'à ce que le fichier soit disponible)
- Structure try-finally pour garantir la libération du sémaphore

```csharp
        if (!File.Exists(_filePath))
        {
            return new List<string>();
        }
```
- Si le fichier n'existe pas, retourne une liste vide

```csharp
        // Lecture avec retry en cas d'erreur
        var retryCount = 3;
        Exception? lastException = null;

        for (int i = 0; i < retryCount; i++)
        {
            try
            {
```
- Système de relance en cas d'erreur (3 tentatives)
- Stocke la dernière exception pour la relancer si nécessaire

```csharp
                // Utilisation de FileShare.Read pour permettre lectures multiples
                using var stream = new FileStream(_filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                using var reader = new StreamReader(stream, Encoding.UTF8);
```
- Ouvre le fichier en lecture avec `FileShare.Read` pour permettre des lectures simultanées
- Utilise `Encoding.UTF8` pour interpréter correctement les caractères du fichier

```csharp
                var lines = new List<string>();
                string? line;
                while ((line = await reader.ReadLineAsync()) != null)
                {
                    if (!string.IsNullOrWhiteSpace(line))
                    {
                        lines.Add(line);
                    }
                }
                return lines;
```
- Lit le fichier ligne par ligne de manière asynchrone
- Ignore les lignes vides ou qui ne contiennent que des espaces
- Retourne la liste des lignes valides

```csharp
            }
            catch (IOException ex) when (i < retryCount - 1)
            {
                lastException = ex;
                await Task.Delay(100 * (i + 1)); // Délai progressif
            }
        }
```
- Capture uniquement les `IOException` et seulement si ce n'est pas la dernière tentative
- Implémente un délai progressif entre les tentatives (100ms, 200ms, 300ms)

```csharp
        throw new InvalidOperationException($"Impossible de lire le fichier après {retryCount} tentatives", lastException);
    }
    finally
    {
        _semaphore.Release();
    }
}
```
- Lève une exception si toutes les tentatives échouent, en encapsulant l'exception originale
- Le bloc `finally` garantit que le sémaphore est toujours libéré, même en cas d'erreur

## Méthode WriteAllLinesAsync
```csharp
public async Task WriteAllLinesAsync(IEnumerable<string> lines)
{
    await _semaphore.WaitAsync();
    try
    {
```
- Méthode asynchrone pour écrire dans le fichier
- Attend d'acquérir le sémaphore pour éviter les écritures concurrentes

```csharp
        // Créer le répertoire si nécessaire
        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }
```
- Vérifie et crée le répertoire parent si nécessaire

```csharp
        // Écriture atomique : écrire dans un fichier temporaire puis renommer
        var tempFile = _filePath + ".tmp";
        var backupFile = _filePath + ".bak";
```
- Définit les chemins pour les fichiers temporaire et de sauvegarde
- L'approche atomique garantit que le fichier n'est jamais dans un état inconsistant

```csharp
        try
        {
            // Créer une sauvegarde de l'ancien fichier
            if (File.Exists(_filePath))
            {
                File.Copy(_filePath, backupFile, true);
            }
```
- Crée une sauvegarde de l'ancien fichier si celui-ci existe

```csharp
            // Écrire dans le fichier temporaire
            using (var stream = new FileStream(tempFile, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(stream, Encoding.UTF8))
            {
                foreach (var line in lines)
                {
                    await writer.WriteLineAsync(line);
                }
                await writer.FlushAsync();
            }
```
- Crée un nouveau fichier temporaire
- Écrit toutes les lignes dans ce fichier temporaire
- Force l'écriture des données mises en tampon avec `FlushAsync()`

```csharp
            // Remplacer l'ancien fichier par le nouveau (opération atomique)
            if (File.Exists(_filePath))
            {
                File.Delete(_filePath);
            }
            File.Move(tempFile, _filePath);
```
- Supprime l'ancien fichier s'il existe
- Renomme le fichier temporaire pour qu'il devienne le fichier principal (opération atomique)

```csharp
            // Supprimer la sauvegarde si tout s'est bien passé
            if (File.Exists(backupFile))
            {
                File.Delete(backupFile);
            }
```
- Supprime le fichier de sauvegarde si l'opération a réussi

```csharp
        }
        catch (Exception)
        {
            // En cas d'erreur, nettoyer et restaurer
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }

            if (File.Exists(backupFile) && !File.Exists(_filePath))
            {
                File.Move(backupFile, _filePath);
            }
            throw;
        }
```
- En cas d'erreur: 
  - Supprime le fichier temporaire s'il existe
  - Restaure la sauvegarde si le fichier principal n'existe plus
  - Relance l'exception

```csharp
    }
    finally
    {
        _semaphore.Release();
    }
}
```
- Libère toujours le sémaphore, même en cas d'erreur

## Méthode Dispose
```csharp
public void Dispose()
{
    _semaphore?.Dispose();
}
```
- Libère le sémaphore lorsque l'instance est supprimée
- L'opérateur `?.` évite une `NullReferenceException` si le sémaphore est null