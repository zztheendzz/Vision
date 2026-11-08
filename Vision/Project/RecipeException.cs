using System;
using System.Collections.Generic;
using System.Text;

namespace Vision.Project
{
    /// <summary>File recipe không mở được. Errors liệt kê MỌI lỗi tìm thấy (không dừng ở lỗi đầu tiên).</summary>
    public sealed class RecipeException : Exception
    {
        public IReadOnlyList<string> Errors { get; }

        public RecipeException(IReadOnlyList<string> errors)
            : base("Không mở được recipe:\n - " + string.Join("\n - ", errors))
            => Errors = errors;

        public RecipeException(string error) : this(new[] { error }) { }
    }
}
