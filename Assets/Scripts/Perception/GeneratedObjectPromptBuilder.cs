using System.Globalization;
using System.Text;
using UnityEngine;

public static class GeneratedObjectPromptBuilder
{
    public const string RoomifyImagePromptVersion = "roomify_image_asset_v2";

    public static string BuildImageStylizationPrompt(GeneratedObjectRequest request)
    {
        if (request == null)
        {
            return string.Empty;
        }

        var builder = new StringBuilder(2048);
        builder.AppendLine("TASK = \"Create one isolated stylized object asset from the reference image while preserving its real-room role and spatial trust.\"");
        builder.AppendLine();
        builder.AppendLine("TARGET_OBJECT = {");
        builder.AppendLine($"  semantic_label: \"{Safe(request.SemanticLabel)}\",");
        builder.AppendLine($"  function_tag: \"{Safe(request.FunctionTag)}\",");
        builder.AppendLine($"  source_anchor_name: \"{Safe(request.SourceAnchorName)}\",");
        builder.AppendLine($"  source_anchor_index: {request.SourceAnchorIndex},");
        builder.AppendLine($"  planned_replacement: \"{Safe(request.PlannedReplacementDisplayName)}\",");
        builder.AppendLine($"  collision_sensitive: {ToBoolLiteral(request.CollisionSensitive)},");
        builder.AppendLine($"  preserve_footprint: {ToBoolLiteral(request.PreserveFootprint)},");
        builder.AppendLine($"  preserve_yaw_orientation: {ToBoolLiteral(request.PreserveYawOrientation)}");
        builder.AppendLine("}");
        builder.AppendLine();
        builder.AppendLine("THEME = {");
        builder.AppendLine($"  id: \"{Safe(request.ThemeId)}\",");
        builder.AppendLine($"  display_name: \"{Safe(request.ThemeDisplayName)}\",");
        builder.AppendLine($"  short_description: \"{Safe(request.ThemeShortDescription)}\"");
        builder.AppendLine("}");
        builder.AppendLine();
        builder.AppendLine("GEOMETRY = {");
        builder.AppendLine($"  dimensions_m: [{FormatFloat(request.Dimensions.x)}, {FormatFloat(request.Dimensions.y)}, {FormatFloat(request.Dimensions.z)}],");
        builder.AppendLine($"  target_length_m: {FormatFloat(request.TargetLengthMeters)},");
        builder.AppendLine($"  target_width_m: {FormatFloat(request.TargetWidthMeters)},");
        builder.AppendLine($"  target_height_m: {FormatFloat(request.TargetHeightMeters)},");
        builder.AppendLine($"  target_aspect_ratio_length_over_width: {FormatFloat(request.TargetAspectRatio)},");
        builder.AppendLine($"  safety_footprint_scale: {FormatFloat(request.SafetyFootprintScale)},");
        builder.AppendLine($"  vertical_fit_mode: \"{request.VerticalFitMode}\",");
        builder.AppendLine($"  scaffold_longest_axis_m: [{FormatFloat(request.ScaffoldLongestAxis.x)}, {FormatFloat(request.ScaffoldLongestAxis.y)}, {FormatFloat(request.ScaffoldLongestAxis.z)}],");
        builder.AppendLine($"  best_view_yaw_degrees: {FormatFloat(request.BestViewYawDegrees)},");
        builder.AppendLine($"  normalized_crop_rect: [{FormatFloat(request.NormalizedCropRect.X)}, {FormatFloat(request.NormalizedCropRect.Y)}, {FormatFloat(request.NormalizedCropRect.Width)}, {FormatFloat(request.NormalizedCropRect.Height)}]");
        builder.AppendLine("}");
        builder.AppendLine();
        builder.AppendLine("REFERENCE_POLICY = [");
        builder.AppendLine("  \"Use the provided source image only as a geometry, material, silhouette, proportion, and viewing-angle reference.\",");
        builder.AppendLine("  \"Do not use the source photo as the final output canvas.\",");
        builder.AppendLine("  \"Recreate only the target object as a clean isolated asset.\",");
        builder.AppendLine("  \"Treat the normalized crop rectangle as a target-object localization hint, not as permission to keep the room background.\"");
        builder.AppendLine("]");
        builder.AppendLine();
        builder.AppendLine("OUTPUT_CONTRACT = {");
        builder.AppendLine("  output_kind: \"single_isolated_object_asset\",");
        builder.AppendLine("  final_background: \"transparent_alpha_required\",");
        builder.AppendLine("  whole_object_visible: true,");
        builder.AppendLine("  generous_padding: true,");
        builder.AppendLine("  seed3d_ready: true");
        builder.AppendLine("}");
        builder.AppendLine();
        builder.AppendLine("IMAGE_INSTRUCTIONS = [");
        builder.AppendLine("  \"Generate exactly one stylized target object, not a complete room scene.\",");
        builder.AppendLine("  \"Preserve the overall silhouette, proportions, and dominant viewing angle of the real object.\",");
        builder.AppendLine("  \"Preserve the explicit target length, width, height, and length/width aspect ratio from GEOMETRY as hard spatial constraints for the later 3D fit.\",");
        builder.AppendLine("  \"Keep the visible footprint within the safety_footprint_scale; do not widen the base or supports beyond the real scaffold footprint.\",");
        builder.AppendLine("  \"Respect vertical_fit_mode so tabletop/contact height stays compatible with the real MRUK scaffold.\",");
        builder.AppendLine("  \"Keep the object readable as its original high-level function.\",");
        builder.AppendLine("  \"Preserve collision-relevant footprint cues, support/contact surfaces, and major leg/base placement.\",");
        builder.AppendLine("  \"Maintain walk-around clearance implied by the current footprint.\",");
        builder.AppendLine("  \"Include the entire object with all important edges and supports visible.\",");
        builder.AppendLine("  \"Return one stylized reference image suitable for image-to-3D generation and scaffold-aware registration.\"");
        builder.AppendLine("]");
        builder.AppendLine();
        builder.AppendLine("NEGATIVE_CONSTRAINTS = [");
        builder.AppendLine("  \"No room background, no floor plane, no wall, no ceiling, no shelves, no chairs, no other furniture.\",");
        builder.AppendLine("  \"No nearby objects from the source image, including books, boxes, board games, clutter, or tabletop props.\",");
        builder.AppendLine("  \"No people, labels, logos, captions, watermarks, extra decorations, cast shadows, contact shadows, or reflections.\",");
        builder.AppendLine("  \"Do not crop off object corners, legs, bases, or other registration-relevant silhouette features.\"");
        builder.AppendLine("]");
        builder.AppendLine();
        builder.AppendLine("GPT_IMAGE_2_WORKER_NOTE = [");
        builder.AppendLine("  \"If the image model cannot directly output transparency, generate the isolated object on a perfectly flat solid #00ff00 chroma-key background.\",");
        builder.AppendLine("  \"The chroma-key background must be one uniform color with no shadows, gradients, texture, reflections, or floor plane.\",");
        builder.AppendLine("  \"Do not use #00ff00 anywhere on the object.\",");
        builder.AppendLine("  \"Remove the chroma-key locally and save the final result as a PNG with alpha at RequestedOutputImagePath.\"");
        builder.AppendLine("]");
        builder.AppendLine();
        builder.Append("OUTPUT_STYLE_HINT = \"");
        builder.Append(Safe(request.AppearancePrompt));
        builder.AppendLine("\"");
        return builder.ToString().TrimEnd();
    }

    private static string Safe(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }

    private static string FormatFloat(float value)
    {
        return value.ToString("0.###", CultureInfo.InvariantCulture);
    }

    private static string ToBoolLiteral(bool value)
    {
        return value ? "true" : "false";
    }
}
