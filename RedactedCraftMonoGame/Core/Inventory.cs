using System;

namespace RedactedCraftMonoGame.Core;

public struct HotbarSlot
{
    public BlockId Id;
    public int Count;
}

public sealed class Inventory
{
    public const int HotbarSize = 9;
    public const int GridCols = 9;
    public const int GridRows = 3;
    public const int GridSize = GridCols * GridRows;
    private readonly HotbarSlot[] _hotbar = new HotbarSlot[HotbarSize];
    private readonly HotbarSlot[] _grid = new HotbarSlot[GridSize];
    private bool _sandboxCatalogBuilt;

    public GameMode Mode { get; private set; } = GameMode.Sandbox;

    public int SelectedIndex { get; private set; }

    public HotbarSlot[] Hotbar => _hotbar;
    public HotbarSlot[] Grid => _grid;

    public BlockId SelectedId => _hotbar[SelectedIndex].Id;

    public int SelectedCount => _hotbar[SelectedIndex].Count;

    public void SetMode(GameMode mode)
    {
        Mode = mode;
        if (Mode == GameMode.Sandbox)
            ClampSandboxStacks();
        if (Mode == GameMode.Sandbox)
            EnsureSandboxCatalog();
    }

    public void Select(int index)
    {
        SelectedIndex = Math.Clamp(index, 0, HotbarSize - 1);
    }

    public void Scroll(int delta)
    {
        if (delta == 0)
            return;
        var next = (SelectedIndex + delta) % HotbarSize;
        if (next < 0)
            next += HotbarSize;
        SelectedIndex = next;
    }

    public void PickBlock(BlockId id)
    {
        if (id == BlockId.Air)
            return;

        // 1. Is it already in the hotbar?
        for (int i = 0; i < HotbarSize; i++)
        {
            if (_hotbar[i].Id == id && _hotbar[i].Count > 0)
            {
                SelectedIndex = i;
                return;
            }
        }

        // 2. Is it in the grid?
        for (int i = 0; i < GridSize; i++)
        {
            if (_grid[i].Id == id && _grid[i].Count > 0)
            {
                // Swap with current hotbar slot
                var temp = _hotbar[SelectedIndex];
                _hotbar[SelectedIndex] = _grid[i];
                _grid[i] = temp;
                return;
            }
        }

        // 3. Sandbox mode: just set it in current slot if not found
        if (Mode == GameMode.Sandbox)
        {
            _hotbar[SelectedIndex].Id = id;
            _hotbar[SelectedIndex].Count = 1;
        }
    }

    public void Add(BlockId id, int amount)
    {
        if (id == BlockId.Air || amount <= 0)
            return;

        var max = GetMaxStack(id, Mode);
        if (max <= 0)
            return;

        // 1. Check existing hotbar stacks
        for (int i = 0; i < _hotbar.Length; i++)
        {
            if (_hotbar[i].Count > 0 && _hotbar[i].Id == id)
            {
                _hotbar[i].Count = Math.Min(max, _hotbar[i].Count + amount);
                return;
            }
        }

        // 2. Check existing grid stacks
        for (int i = 0; i < _grid.Length; i++)
        {
            if (_grid[i].Count > 0 && _grid[i].Id == id)
            {
                _grid[i].Count = Math.Min(max, _grid[i].Count + amount);
                return;
            }
        }

        // 3. Check empty hotbar slots
        for (int i = 0; i < _hotbar.Length; i++)
        {
            if (_hotbar[i].Count == 0)
            {
                _hotbar[i].Id = id;
                _hotbar[i].Count = Math.Min(max, amount);
                if (_hotbar[SelectedIndex].Count == 0)
                    SelectedIndex = i;
                return;
            }
        }

        // 4. Check empty grid slots
        for (int i = 0; i < _grid.Length; i++)
        {
            if (_grid[i].Count == 0)
            {
                _grid[i].Id = id;
                _grid[i].Count = Math.Min(max, amount);
                return;
            }
        }
    }

    public bool TryConsumeSelected(int amount)
    {
        if (amount <= 0)
            return true;
        if (Mode == GameMode.Sandbox)
            return true;

        ref var slot = ref _hotbar[SelectedIndex];
        if (slot.Count < amount || slot.Id == BlockId.Air)
            return false;

        slot.Count -= amount;
        if (slot.Count <= 0)
        {
            slot.Count = 0;
            slot.Id = BlockId.Air;
        }
        return true;
    }

    private static int GetMaxStack(BlockId id, GameMode mode)
    {
        return mode == GameMode.Sandbox ? 1 : 64;
    }

    private void ClampSandboxStacks()
    {
        for (int i = 0; i < _hotbar.Length; i++)
        {
            if (_hotbar[i].Count > 1)
                _hotbar[i].Count = 1;
        }
        for (int i = 0; i < _grid.Length; i++)
        {
            if (_grid[i].Count > 1)
                _grid[i].Count = 1;
        }
    }

    private void EnsureSandboxCatalog()
    {
        if (_sandboxCatalogBuilt)
            return;

        var index = 0;
        foreach (var def in BlockRegistry.All)
        {
            if (def.Id == BlockId.Air || !def.IsVisibleInInventory)
                continue;
            if (index >= _grid.Length)
                break;
            _grid[index].Id = def.Id;
            _grid[index].Count = 1;
            index++;
        }

        for (int i = index; i < _grid.Length; i++)
        {
            _grid[i].Id = BlockId.Air;
            _grid[i].Count = 0;
        }

        _sandboxCatalogBuilt = true;
    }
}
