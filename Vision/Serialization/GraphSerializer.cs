using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Vision.Graph;
using Vision.Project;
using Vision.Serialization;
using Vision.Tools.Registry;

namespace Vision.Tools
{
    /// <summary>
    /// Lưu / mở graph dưới dạng JSON (recipe).
    ///
    /// Nguyên tắc:
    ///  - Chỉ lưu DỮ LIỆU cấu hình: node (Id, tool, vị trí, tham số) và dây nối. Không lưu kết quả chạy, không lưu ảnh.
    ///  - Tool tham chiếu bằng ToolDescriptor.Id (ổn định), không bằng tên lớp C#.
    ///  - Mở file là ghép lại graph bằng chính API đã có (AddNode, SetParameter, Connect) nên mọi luật
    ///    (kiểu cổng, không vòng, tham số trong khoảng) được kiểm tra lại, file sửa tay sai cũng không lọt.
    ///  - Lỗi thì gom hết rồi ném RecipeException; KHÔNG mở nửa vời (chạy sản xuất với graph thiếu node là nguy hiểm).
    ///  - Ghi file an toàn: ghi ra file tạm rồi thay thế, mất điện giữa chừng không làm hỏng recipe cũ.
    /// </summary>
    public static class GraphSerializer
    {
        public const int CurrentFormatVersion = 1;

        private static readonly JsonSerializerOptions Options = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never,
            Converters = { new JsonStringEnumConverter() }
        };

        // ================= GHI =================

        public static string ToJson(VisionGraph graph, string name = "")
        {
            var dto = ToDto(graph, name);
            return JsonSerializer.Serialize(dto, Options);
        }

        public static void Save(VisionGraph graph, string path, string? name = null)
        {
            string json = ToJson(graph, name ?? Path.GetFileNameWithoutExtension(path));

            string? dir = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            string temp = path + ".tmp";
            File.WriteAllText(temp, json);
            if (File.Exists(path)) File.Replace(temp, path, null);
            else File.Move(temp, path);
        }

        public static RecipeDto ToDto(VisionGraph graph, string name = "")
        {
            var dto = new RecipeDto { Name = name };

            foreach (var node in graph.Nodes)
            {
                var n = new NodeDto { Id = node.Id, Tool = node.ToolName, X = node.X, Y = node.Y, Description = node.Description ?? "" };
                foreach (var (key, value) in node.Parameters.OrderBy(p => p.Key, StringComparer.Ordinal))
                    n.Parameters[key] = JsonSerializer.SerializeToElement(value, Options);
                dto.Nodes.Add(n);
            }

            // Sắp xếp để cùng một graph luôn ra cùng một file (so sánh/diff dễ)
            foreach (var c in graph.Connections
                .OrderBy(c => c.From.Owner.Id).ThenBy(c => c.From.Name, StringComparer.Ordinal)
                .ThenBy(c => c.To.Owner.Id).ThenBy(c => c.To.Name, StringComparer.Ordinal))
            {
                dto.Connections.Add(new ConnectionDto
                {
                    From = new EndpointDto { Node = c.From.Owner.Id, Port = c.From.Name },
                    To = new EndpointDto { Node = c.To.Owner.Id, Port = c.To.Name }
                });
            }
            return dto;
        }

        // ================= ĐỌC =================

        public static LoadedRecipe Load(string path)
        {
            string json;
            try { json = File.ReadAllText(path); }
            catch (Exception ex) { throw new RecipeException($"Không đọc được file '{path}': {ex.Message}"); }
            return FromJson(json);
        }

        public static LoadedRecipe FromJson(string json)
        {
            RecipeDto? dto;
            try { dto = JsonSerializer.Deserialize<RecipeDto>(json, Options); }
            catch (JsonException ex) { throw new RecipeException($"File không đúng định dạng JSON: {ex.Message}"); }

            if (dto == null) throw new RecipeException("File rỗng");
            return FromDto(dto);
        }

        public static LoadedRecipe FromDto(RecipeDto dto)
        {
            if (dto.FormatVersion > CurrentFormatVersion)
                throw new RecipeException(
                    $"Recipe phiên bản {dto.FormatVersion} mới hơn phần mềm này (hỗ trợ đến {CurrentFormatVersion}). Hãy cập nhật phần mềm.");
            if (dto.FormatVersion < 1)
                throw new RecipeException($"FormatVersion không hợp lệ: {dto.FormatVersion}");
            // Sau này có bản 2: gọi hàm nâng cấp dto từ 1 lên 2 ở đây trước khi dựng graph.

            var errors = new List<string>();
            var warnings = new List<string>();
            var graph = new VisionGraph();

            try
            {
                var byId = new Dictionary<Guid, VisionNode>();

                // ---- Node ----
                foreach (var n in dto.Nodes)
                {
                    string label = $"Node {n.Id} ({n.Tool})";

                    if (n.Id == Guid.Empty) { errors.Add($"{label}: thiếu Id"); continue; }
                    if (byId.ContainsKey(n.Id)) { errors.Add($"{label}: Id bị trùng"); continue; }
                    if (!ToolRegistry.TryGet(n.Tool, out _))
                    {
                        errors.Add($"{label}: không có tool '{n.Tool}' trong phần mềm này");
                        continue;
                    }

                    VisionNode node;
                    try { node = graph.AddNode(n.Tool, n.X, n.Y, n.Id); }
                    catch (Exception ex) { errors.Add($"{label}: không tạo được ({ex.Message})"); continue; }

                    node.Description = n.Description ?? "";
                    byId[n.Id] = node;
                    ApplyParameters(node, n, label, errors, warnings);
                }

                // ---- Dây nối ----
                foreach (var c in dto.Connections)
                {
                    string label = $"Dây {c.From.Node}.{c.From.Port} -> {c.To.Node}.{c.To.Port}";

                    if (!byId.TryGetValue(c.From.Node, out var fromNode)) { errors.Add($"{label}: không thấy node nguồn"); continue; }
                    if (!byId.TryGetValue(c.To.Node, out var toNode)) { errors.Add($"{label}: không thấy node đích"); continue; }

                    try
                    {
                        graph.Connect(fromNode.Output(c.From.Port), toNode.Input(c.To.Port));
                    }
                    catch (Exception ex)
                    {
                        errors.Add($"{label}: {ex.Message}");
                    }
                }
            }
            catch
            {
                graph.Dispose();
                throw;
            }

            if (errors.Count > 0)
            {
                graph.Dispose();
                throw new RecipeException(errors);
            }
            return new LoadedRecipe(dto.Name, graph, warnings);
        }

        private static void ApplyParameters(
            VisionNode node, NodeDto dto, string label, List<string> errors, List<string> warnings)
        {
            foreach (var (name, value) in dto.Parameters)
            {
                var def = node.Tool.Parameters.FirstOrDefault(p => p.Name == name);
                if (def == null)
                {
                    // Tool bản mới có thể đã bỏ tham số này: bỏ qua nhưng báo cho người dùng biết
                    warnings.Add($"{label}: bỏ qua tham số '{name}' (tool không còn tham số này)");
                    continue;
                }

                try { node.SetParameter(name, value); }
                catch (Exception ex) { errors.Add($"{label}: tham số '{name}' không hợp lệ ({ex.Message})"); }
            }

            // Tham số tool có mà file không có (tool bản mới thêm): dùng mặc định, báo để người dùng xem lại
            foreach (var def in node.Tool.Parameters)
                if (!dto.Parameters.ContainsKey(def.Name))
                    warnings.Add($"{label}: file chưa có tham số '{def.Name}', dùng mặc định {def.Default}");
        }
    }
}
