using System;
using System.Collections.Generic;
using System.Text;
using Vision.Graph;

namespace Vision.Project
{
    /// <summary>Kết quả mở recipe. Người nhận sở hữu Graph (phải Dispose). Warnings: những điều đã tự xử lý, nên cho người dùng thấy.</summary>
    public sealed record LoadedRecipe(string Name, VisionGraph Graph, IReadOnlyList<string> Warnings);
}
