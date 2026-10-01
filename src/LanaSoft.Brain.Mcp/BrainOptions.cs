namespace LanaSoft.Brain.Mcp;

/// <summary>Configuration de la passerelle (section « Brain » d'appsettings / variables Brain__*).</summary>
public sealed class BrainOptions
{
    public const string Section = "Brain";

    /// <summary>
    /// URL publique de la passerelle, ex. https://brain.example.com/mcp : c'est aussi l'audience
    /// exigée dans les jetons. Obligatoire.
    /// </summary>
    public string Resource { get; set; } = "";

    /// <summary>Émetteur des jetons, ex. https://auth.example.com/realms/example. Obligatoire.</summary>
    public string Authority { get; set; } = "";

    /// <summary>
    /// Document de découverte OpenID lu par la passerelle, quand elle doit joindre le fournisseur
    /// d'identité par une autre adresse que son émetteur public (réseau Docker de la démo). Vide :
    /// dérivé de <see cref="Authority"/>.
    /// </summary>
    public string MetadataAddress { get; set; } = "";

    /// <summary>Exige HTTPS pour le document de découverte ; false uniquement en local.</summary>
    public bool RequireHttpsMetadata { get; set; } = true;

    /// <summary>
    /// Propriétaire des mémoires écrites par un agent (jeton client credentials, sans utilisateur) :
    /// le nom d'utilisateur du propriétaire chez le fournisseur d'identité. Obligatoire.
    /// </summary>
    public string AgentOwner { get; set; } = "";

    /// <summary>
    /// Noms courts des clients dont l'identifiant OAuth est une URL (Client ID Metadata Document,
    /// comme Claude Code) : ce nom remplace l'azp dans agent_id. Une liste et non un dictionnaire,
    /// car « : » sépare les clés de configuration .NET.
    /// </summary>
    public List<AgentAlias> AgentAliases { get; set; } = [];

    /// <summary>API Mem0, joignable uniquement depuis la passerelle.</summary>
    public string Mem0Url { get; set; } = "http://mem0-api:8000";

    /// <summary>Clé m0sk_ de l'API Mem0 (secret, jamais dans git).</summary>
    public string Mem0ApiKey { get; set; } = "";

    /// <summary>Longueur maximale d'un fait (remember).</summary>
    public int MaxFactLength { get; set; } = 2_000;

    /// <summary>Longueur maximale d'un résumé de session.</summary>
    public int MaxSummaryLength { get; set; } = 8_000;

    /// <summary>Faits proches renvoyés au plus pour chaque fait extrait par session_summary.</summary>
    public int RelatedLimit { get; set; } = 3;

    /// <summary>
    /// Similarité cosinus minimale d'un fait proche (score sémantique de Mem0, pas son score
    /// hybride). Les scores des candidats sont journalisés pour la régler.
    /// </summary>
    public double RelatedMinScore { get; set; } = 0.75;

    /// <summary>Requêtes MCP autorisées par identité et par minute.</summary>
    public int RequestsPerMinute { get; set; } = 60;

    /// <summary>Arrête le démarrage si un réglage obligatoire manque, plutôt que de refuser tous les jetons.</summary>
    public void Validate()
    {
        var missing = new[] { (nameof(Resource), Resource), (nameof(Authority), Authority), (nameof(AgentOwner), AgentOwner) }
            .Where(s => string.IsNullOrWhiteSpace(s.Item2))
            .Select(s => $"{Section}:{s.Item1}")
            .ToList();
        if (missing.Count > 0)
            throw new InvalidOperationException($"Configuration incomplète : {string.Join(", ", missing)}.");
    }
}

/// <summary>Nom court d'un client OAuth, voir <see cref="BrainOptions.AgentAliases"/>.</summary>
public sealed class AgentAlias
{
    /// <summary>Identifiant du client tel qu'il figure dans l'azp du jeton.</summary>
    public string ClientId { get; set; } = "";

    /// <summary>Nom enregistré dans agent_id.</summary>
    public string Name { get; set; } = "";
}
