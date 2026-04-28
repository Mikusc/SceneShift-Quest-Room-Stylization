using System.Globalization;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

public static class GeneratedObjectPromptBuilder
{
    public const string RoomifyImagePromptVersion = "roomify_image_asset_v3_style_keywords";

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
        builder.AppendLine($"  replica_name: \"{Safe(Coalesce(request.PlannedReplicaName, request.PlannedReplacementDisplayName))}\",");
        builder.AppendLine($"  replica_function: \"{Safe(Coalesce(request.PlannedReplicaFunction, request.FunctionTag))}\",");
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
        builder.AppendLine("STYLE_INTENT = {");
        builder.AppendLine($"  user_intent: \"{Safe(request.UserStyleIntent)}\",");
        builder.AppendLine($"  source: \"{Safe(request.StyleIntentSource)}\",");
        builder.AppendLine($"  global_style_summary: \"{Safe(request.GlobalStyleSummary)}\",");
        builder.Append("  style_keywords: ");
        AppendStringList(builder, request.StyleKeywords);
        builder.AppendLine(",");
        builder.Append("  material_keywords: ");
        AppendStringList(builder, request.MaterialKeywords);
        builder.AppendLine(",");
        builder.Append("  color_keywords: ");
        AppendStringList(builder, request.ColorKeywords);
        builder.AppendLine(",");
        builder.Append("  motif_keywords: ");
        AppendStringList(builder, request.MotifKeywords);
        builder.AppendLine(",");
        builder.Append("  negative_style_keywords: ");
        AppendStringList(builder, request.NegativeStyleKeywords);
        builder.AppendLine(",");
        builder.AppendLine($"  object_style_directive: \"{Safe(request.ObjectStyleDirective)}\"");
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
        builder.AppendLine($"  \"{Safe(BuildSemanticInferenceInstruction(request))}\",");
        builder.AppendLine("  \"Transform the original object into the named replica while preserving the original function tag.\",");
        builder.AppendLine("  \"If STYLE_INTENT.user_intent is non-empty, treat STYLE_INTENT as the primary visual style layer over the preset ThemeProfile.\",");
        builder.AppendLine("  \"Use STYLE_INTENT keywords to keep arbitrary user themes coherent across objects without changing the object's function or spatial constraints.\",");
        builder.AppendLine("  \"Use OUTPUT_STYLE_HINT as the Roomify appearance prompt: it controls shape language, materials, color palette, and texture details.\",");
        builder.AppendLine("  \"Preserve the overall silhouette, proportions, and dominant viewing angle of the real object.\",");
        builder.AppendLine("  \"Preserve the explicit target length, width, height, and length/width aspect ratio from GEOMETRY as hard spatial constraints for the later 3D fit.\",");
        builder.AppendLine("  \"Keep the visible footprint within the safety_footprint_scale; do not widen the base or supports beyond the real scaffold footprint.\",");
        builder.AppendLine("  \"Respect vertical_fit_mode so tabletop/contact height stays compatible with the real MRUK scaffold.\",");
        builder.AppendLine("  \"Keep the object readable as its original high-level function.\",");
        builder.AppendLine($"  \"{Safe(BuildFunctionPreservationInstruction(request))}\",");
        builder.AppendLine("  \"Maintain walk-around clearance implied by the current footprint.\",");
        builder.AppendLine("  \"Include the entire object with all important edges and supports visible.\",");
        builder.AppendLine("  \"Return one stylized reference image suitable for image-to-3D generation and scaffold-aware registration.\"");
        builder.AppendLine("]");
        builder.AppendLine();
        builder.AppendLine("NEGATIVE_CONSTRAINTS = [");
        builder.AppendLine($"  \"{Safe(BuildOtherFurnitureNegativeConstraint(request))}\",");
        builder.AppendLine("  \"No nearby objects from the source image, including books, boxes, board games, clutter, or tabletop props.\",");
        builder.AppendLine("  \"No people, labels, logos, captions, watermarks, extra decorations, cast shadows, contact shadows, or reflections.\",");
        builder.AppendLine("  \"Do not crop off object corners, legs, bases, or other registration-relevant silhouette features.\",");
        builder.AppendLine("  \"Do not let STYLE_INTENT override explicit geometry, footprint, yaw, safety, or functional constraints.\"");
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
        builder.Append(Safe(BuildOutputStyleHint(request)));
        builder.AppendLine("\"");
        return builder.ToString().TrimEnd();
    }

    private static string BuildOutputStyleHint(GeneratedObjectRequest request)
    {
        var builder = new StringBuilder(512);
        builder.Append(request.AppearancePrompt ?? string.Empty);

        if (!string.IsNullOrWhiteSpace(request.UserStyleIntent))
        {
            builder.Append(" Runtime user style intent: ");
            builder.Append(request.UserStyleIntent.Trim());
            builder.Append(". Global style summary: ");
            builder.Append(request.GlobalStyleSummary);
            builder.Append(". Style keywords: ");
            builder.Append(JoinList(request.StyleKeywords));
            builder.Append(". Materials: ");
            builder.Append(JoinList(request.MaterialKeywords));
            builder.Append(". Colors and lighting: ");
            builder.Append(JoinList(request.ColorKeywords));
            builder.Append(". Motifs: ");
            builder.Append(JoinList(request.MotifKeywords));
            builder.Append(". Avoid: ");
            builder.Append(JoinList(request.NegativeStyleKeywords));
            if (!string.IsNullOrWhiteSpace(request.ObjectStyleDirective))
            {
                builder.Append(". Directive: ");
                builder.Append(request.ObjectStyleDirective.Trim());
            }
        }

        return builder.ToString().Trim();
    }

    private static string BuildFunctionPreservationInstruction(GeneratedObjectRequest request)
    {
        var semanticLabel = request != null ? request.SemanticLabel : string.Empty;
        if (string.Equals(semanticLabel, "other", System.StringComparison.OrdinalIgnoreCase))
        {
            return "The spatial label is OTHER, so infer the target object's visual category and functional role from the reference image, keep the generated asset conservative and close to the visible source shape, and preserve footprint, contact surface, stable base placement, dominant orientation, and original affordance if recognizable.";
        }

        if (string.Equals(semanticLabel, "storage", System.StringComparison.OrdinalIgnoreCase))
        {
            return "Preserve storage volume cues, front/back orientation, stable base contact, doors/drawers/shelves as readable storage features, and collision-relevant footprint.";
        }

        if (string.Equals(semanticLabel, "screen", System.StringComparison.OrdinalIgnoreCase))
        {
            return "Preserve display-surface cues, front-facing orientation, readable screen or board plane, stable support/base placement, and collision-relevant footprint.";
        }

        if (string.Equals(semanticLabel, "table", System.StringComparison.OrdinalIgnoreCase))
        {
            return "Preserve collision-relevant footprint cues, support/contact surfaces, tabletop height, and major leg/base placement.";
        }

        if (string.Equals(semanticLabel, "seating", System.StringComparison.OrdinalIgnoreCase))
        {
            return "Preserve sit-able surface cues, back/support orientation, stable base contact, and collision-relevant footprint.";
        }

        if (string.Equals(semanticLabel, "bed", System.StringComparison.OrdinalIgnoreCase))
        {
            return "Preserve sleepable horizontal surface cues, head/foot orientation, stable base contact, mattress or platform volume, and collision-relevant footprint.";
        }

        if (string.Equals(semanticLabel, "lamp", System.StringComparison.OrdinalIgnoreCase))
        {
            return "Preserve lamp-like lighting function, visible shade/head or emissive element, stable base/stand contact, upright orientation, and collision-relevant footprint.";
        }

        if (string.Equals(semanticLabel, "plant", System.StringComparison.OrdinalIgnoreCase))
        {
            return "Preserve plant-like organic silhouette, pot/base contact if present, upright growth direction, stable placement, and collision-relevant footprint.";
        }

        return "Preserve collision-relevant footprint cues, contact surfaces, stable base placement, and the object's original functional affordance.";
    }

    private static string BuildSemanticInferenceInstruction(GeneratedObjectRequest request)
    {
        var semanticLabel = request != null ? request.SemanticLabel : string.Empty;
        return string.Equals(semanticLabel, "other", System.StringComparison.OrdinalIgnoreCase)
            ? "The spatial system labeled this target as OTHER; use visual reasoning over the reference image to infer what physical object it is, then stylize that same object instead of inventing a new furniture category."
            : "Use semantic_label and function_tag as the target object's category and functional contract.";
    }

    private static string BuildOtherFurnitureNegativeConstraint(GeneratedObjectRequest request)
    {
        var semanticLabel = request != null ? request.SemanticLabel : string.Empty;
        if (string.Equals(semanticLabel, "other", System.StringComparison.OrdinalIgnoreCase))
        {
            return "No room background, no floor plane, no wall, no ceiling, no unrelated furniture, no extra props; include only the visually inferred target object.";
        }

        if (string.Equals(semanticLabel, "storage", System.StringComparison.OrdinalIgnoreCase))
        {
            return "No room background, no floor plane, no wall, no ceiling, no tables, no chairs, no screens, no other non-target furniture.";
        }

        if (string.Equals(semanticLabel, "screen", System.StringComparison.OrdinalIgnoreCase))
        {
            return "No room background, no floor plane, no wall, no ceiling, no cabinets, no tables, no chairs, no unrelated display devices.";
        }

        if (string.Equals(semanticLabel, "table", System.StringComparison.OrdinalIgnoreCase))
        {
            return "No room background, no floor plane, no wall, no ceiling, no shelves, no cabinets, no chairs, no other furniture.";
        }

        if (string.Equals(semanticLabel, "seating", System.StringComparison.OrdinalIgnoreCase))
        {
            return "No room background, no floor plane, no wall, no ceiling, no table, no cabinet, no unrelated furniture; include only the target seat or couch.";
        }

        if (string.Equals(semanticLabel, "bed", System.StringComparison.OrdinalIgnoreCase))
        {
            return "No room background, no floor plane, no wall, no ceiling, no tables, no cabinets, no chairs; include only the target bed-like object.";
        }

        if (string.Equals(semanticLabel, "lamp", System.StringComparison.OrdinalIgnoreCase))
        {
            return "No room background, no floor plane, no wall, no ceiling, no table, no plant, no unrelated furniture; include only the target lamp.";
        }

        if (string.Equals(semanticLabel, "plant", System.StringComparison.OrdinalIgnoreCase))
        {
            return "No room background, no floor plane, no wall, no ceiling, no lamp, no table, no unrelated furniture; include only the target plant and its pot/base if visible.";
        }

        return "No room background, no floor plane, no wall, no ceiling, no other non-target furniture.";
    }

    private static string Safe(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }

    private static void AppendStringList(StringBuilder builder, List<string> values)
    {
        builder.Append('[');
        if (values != null)
        {
            for (var index = 0; index < values.Count; index++)
            {
                if (index > 0)
                {
                    builder.Append(", ");
                }

                builder.Append('"');
                builder.Append(Safe(values[index]));
                builder.Append('"');
            }
        }

        builder.Append(']');
    }

    private static string JoinList(List<string> values)
    {
        return values == null || values.Count == 0 ? "none" : string.Join(", ", values);
    }

    private static string Coalesce(string primary, string fallback)
    {
        return !string.IsNullOrWhiteSpace(primary) ? primary : fallback;
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
