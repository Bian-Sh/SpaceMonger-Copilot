using SpaceMonger.Core.Models;

namespace SpaceMonger.Core.Services.Treemap;

public interface ITreemapLayoutEngine
{
    List<TreemapNode> ComputeLayout(FileEntry root, float width, float height, int maxDepth);
}
