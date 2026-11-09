using System.Collections.Generic;
using Godot;
using FishingSim.Tournaments;

namespace FishingSim.UI;

/// <summary>Aligned table of tournament standings (rank, name, fish, score); the player's row is highlighted.</summary>
public static class StandingsGrid
{
    public static GridContainer Create(IEnumerable<Standing> standings)
    {
        var grid = new GridContainer { Columns = 4 };
        grid.AddThemeConstantOverride("h_separation", 18);
        grid.AddThemeConstantOverride("v_separation", 3);
        foreach (var s in standings)
        {
            AddCell(grid, $"{s.Rank}.", s.IsPlayer, HorizontalAlignment.Right, 34);
            AddCell(grid, s.Name, s.IsPlayer, HorizontalAlignment.Left, 120);
            AddCell(grid, $"{s.Fish} ryb", s.IsPlayer, HorizontalAlignment.Right, 60);
            AddCell(grid, $"{s.Score:0.00} kg", s.IsPlayer, HorizontalAlignment.Right, 90);
        }
        return grid;
    }

    private static void AddCell(GridContainer grid, string text, bool highlight, HorizontalAlignment align, float minWidth)
    {
        var label = new Label { Text = text, HorizontalAlignment = align, CustomMinimumSize = new Vector2(minWidth, 0) };
        if (highlight)
            label.AddThemeColorOverride("font_color", new Color(1f, 0.9f, 0.4f));
        grid.AddChild(label);
    }
}
