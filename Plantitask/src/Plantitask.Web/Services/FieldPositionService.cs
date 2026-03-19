using Blazored.LocalStorage;
using System.Text.Json;
using Plantitask.Web.Interfaces;
using Plantitask.Web.Models;

namespace Plantitask.Web.Services;

public class FieldPositionService : IFieldPositionService
{
    private readonly ILocalStorageService _localStorage;
    private const string KeyPrefix = "field_positions_";

    public FieldPositionService(ILocalStorageService localStorage)
    {
        _localStorage = localStorage;
    }

    public async Task<Dictionary<string, TreePosition>> GetPositionsAsync(Guid userId)
    {
        try
        {
            var key = KeyPrefix + userId;
            var json = await _localStorage.GetItemAsStringAsync(key);
            if (string.IsNullOrEmpty(json)) return new();
            return JsonSerializer.Deserialize<Dictionary<string, TreePosition>>(json) ?? new();
        }
        catch
        {
            return new();
        }
    }

    public async Task SavePositionAsync(Guid userId, string groupId, double x, double y)
    {
        var positions = await GetPositionsAsync(userId);
        positions[groupId] = new TreePosition { X = x, Y = y };

        var key = KeyPrefix + userId;
        var json = JsonSerializer.Serialize(positions);
        await _localStorage.SetItemAsStringAsync(key, json);
    }

    public async Task SaveAllPositionsAsync(Guid userId, Dictionary<string, TreePosition> positions)
    {
        var key = KeyPrefix + userId;
        var json = JsonSerializer.Serialize(positions);
        await _localStorage.SetItemAsStringAsync(key, json);
    }
}

