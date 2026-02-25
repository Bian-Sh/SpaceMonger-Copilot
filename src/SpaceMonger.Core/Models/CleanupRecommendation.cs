namespace SpaceMonger.Core.Models;

using SpaceMonger.Core.Enums;

/// <summary>
/// Represents a cleanup recommendation for a scanned file or folder.
/// </summary>
public class CleanupRecommendation
{
    /// <summary>
    /// Gets or sets the unique recommendation identifier (e.g., "REC-001").
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the file system path of the file or folder to remove.
    /// </summary>
    public string TargetPath { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the reference to the scanned file or folder entry.
    /// </summary>
    public FileEntry? Entry { get; set; }

    /// <summary>
    /// Gets or sets the size in bytes of the target.
    /// </summary>
    public long Size { get; set; }

    /// <summary>
    /// Gets or sets the cleanup category for this recommendation.
    /// </summary>
    public RecommendationCategory Category { get; set; }

    /// <summary>
    /// Gets or sets the safety rating indicating the risk level of cleanup.
    /// </summary>
    public SafetyRating SafetyRating { get; set; }

    /// <summary>
    /// Gets or sets the human-readable explanation for this recommendation.
    /// </summary>
    public string Explanation { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether the user has accepted this recommendation.
    /// When set to true, IsDismissed is automatically set to false.
    /// </summary>
    private bool _isAccepted;
    public bool IsAccepted
    {
        get => _isAccepted;
        set
        {
            _isAccepted = value;
            if (value)
            {
                _isDismissed = false;
            }
        }
    }

    /// <summary>
    /// Gets or sets a value indicating whether the user has dismissed this recommendation.
    /// When set to true, IsAccepted is automatically set to false.
    /// </summary>
    private bool _isDismissed;
    public bool IsDismissed
    {
        get => _isDismissed;
        set
        {
            _isDismissed = value;
            if (value)
            {
                _isAccepted = false;
            }
        }
    }
}
