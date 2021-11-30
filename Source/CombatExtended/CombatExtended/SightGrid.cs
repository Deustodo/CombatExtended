﻿using System;
using RimWorld;
using Verse;
using UnityEngine;
using System.Runtime.InteropServices;
using System.Diagnostics;

namespace CombatExtended
{
    /*
     * -----------------------------
     *
     *
     * ------ Important note -------
     * 
     * when casting update the grid at a regualar intervals for a pawn/Thing or risk exploding value issues.
     */
    [StaticConstructorOnStartup]
    public class SightGrid
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct SightRecord
        {
            public short sig;            
            public int expireAt;
            public short count;
            public short countPrev;
            public Vector2 direction;
            public Vector2 directionPrev;
        }

        private IntVec3 center;
        private float updateInterval;
        private short sig = 13;
        private CellIndices cellIndices;
        private SightRecord[] grid;        
        private Faction faction;
        private Map map;
        private int mapSizeX;
        private int mapSizeZ;
        private int mapCellNum;

        public Faction Faction
        {
            get => faction;
        }

        public SightGrid(Map map, Faction faction, float updateInterval)
        {
            cellIndices = map.cellIndices;
            mapSizeX = (int)map.Size.x;
            mapSizeZ = (int)map.Size.z;
            mapCellNum = mapSizeX * mapSizeZ;
            grid = new SightRecord[map.cellIndices.NumGridCells];            
            this.updateInterval = updateInterval;
            this.map = map;
            this.faction = faction;            
            for (int i = 0; i < grid.Length; i++)
            {
                grid[i] = new SightRecord()
                {
                    sig = -1,
                    expireAt = 0,
                    direction = Vector3.zero
                };
            }
        }

        public float this[IntVec3 cell]
        {
            get => this[cellIndices.CellToIndex(cell)];            
        }

        public float this[int index]
        {
            get
            {
                if (index >= 0 && index < mapCellNum)
                {
                    SightRecord record = grid[index];
                    if(record.expireAt - GenTicks.TicksGame >= -updateInterval)
                        return Math.Max(record.count, record.countPrev);
                }
                return 0;
            }
        }

        public void Set(IntVec3 cell) => Set(cellIndices.CellToIndex(cell));
        public void Set(int index)
        {
            if (index >= 0 && index < mapCellNum)
            {
                SightRecord record = grid[index];
                IntVec3 cell = cellIndices.IndexToCell(index);
                if (record.sig != sig)
                {
                    float t = record.expireAt - GenTicks.TicksGame;
                    if (t > 0.0f)
                    {
                        record.count += 1;
                        record.direction.x += cell.x - center.x;
                        record.direction.y += cell.z - center.z;
                    }
                    else if (t >= -updateInterval)
                    {
                        record.expireAt = (int)(GenTicks.TicksGame + updateInterval);

                        record.countPrev = record.count;
                        record.directionPrev = record.direction;

                        record.direction.x = cell.x - center.x;
                        record.direction.y = cell.z - center.z;
                        record.count = 1;
                    }
                    else
                    {
                        record.expireAt = (int)(GenTicks.TicksGame + updateInterval);

                        record.countPrev = 0;
                        record.directionPrev = Vector2.zero;

                        record.direction.x = cell.x - center.x;
                        record.direction.y = cell.z - center.z;
                        record.count = 1;
                    }
                    record.sig = sig;
                    grid[index] = record;
                }
            }
        }

        public Vector2 GetDirectionAt(IntVec3 cell) => GetDirectionAt(cellIndices.CellToIndex(cell));        
        public Vector2 GetDirectionAt(int index)
        {
            if (index >= 0 && index < mapCellNum)
            {
                SightRecord record = grid[index];
                if (record.expireAt - GenTicks.TicksGame >= -updateInterval)
                {
                    if (record.count >= record.countPrev)
                        return record.direction;
                    else
                        return record.directionPrev;
                }
            }
            return Vector2.zero;
        }

        public Vector2 GetDirectionAt(IntVec3 cell, out float enemies) => GetDirectionAt(cellIndices.CellToIndex(cell), out enemies);
        public Vector2 GetDirectionAt(int index, out float enemies)
        {
            if (index >= 0 && index < mapCellNum)
            {
                SightRecord record = grid[index];
                if (record.expireAt - GenTicks.TicksGame >= -updateInterval)
                {
                    if (record.count >= record.countPrev)
                    {
                        enemies = record.count;
                        return record.direction;
                    }
                    else
                    {
                        enemies = record.countPrev;
                        return record.directionPrev;
                    }
                }
            }
            enemies = 0;
            return Vector2.zero;
        }

        public bool HasCover(int index) => HasCover(cellIndices.IndexToCell(index));
        public bool HasCover(IntVec3 cell)
        {
            if (cell.InBounds(map))
            {
                SightRecord record = grid[cellIndices.CellToIndex(cell)];
                if (record.expireAt - GenTicks.TicksGame >= -updateInterval)
                {
                    Vector2 direction = record.direction.normalized * -1f;
                    IntVec3 endPos = cell + new Vector3(direction.x * 5, 0, direction.y * 5).ToIntVec3();
                    foreach (IntVec3 c in GenSight.PointsOnLineOfSight(cell, endPos))
                    {
                        if (c.InBounds(map))
                        {
                            Thing cover = c.GetCover(map);                            
                            if (cover != null && cover.def.Fillage == FillCategory.Partial && cover.def.category != ThingCategory.Plant)
                                return true;
                        }
                    }
                }
            }
            return false;
        }
        
        public float GetCellSightCoverRating(int index) => GetCellSightCoverRating(cellIndices.IndexToCell(index));
        public float GetCellSightCoverRating(IntVec3 cell)
        {
            float enemies, val = 0f;
            Vector2 direction = GetDirectionAt(cell, out enemies);
            if (enemies > 0)
            {
                // Mathf.Log(64 - Mathf.Min(grid.GetDirectionAt(cell).magnitude / (0.5f * enemies), 64)) * enemies / 2f, 2f);
                // Or
                // Log2({64 - Min[64, direction.Magnitude / (0.25f * enemies)]} * enemies / 2f)
                //
                // This is a very fast aproximation of the above equation.
                int magSqr = (int)(direction.sqrMagnitude / (0.25f * enemies * enemies) * 2f);
                if (magSqr > sqrtLookup.Length)
                    return 0f;
                val = (64f - sqrtLookup[magSqr]) * Mathf.Min(enemies, 10f) / 2f;
                val = log2Lookup[(int)(val * 10)];
                if (HasCover(cell))
                    val *= 0.5f;
            }
            return val;
        }

        public void Next(IntVec3 center)
        {
            sig += 1;
            this.center = center;
        }

        private static float[] log2Lookup = new float[3420];
        private static float[] sqrtLookup = new float[8198];

        static SightGrid()
        {
            for (int i = 1; i < log2Lookup.Length; i++)
                log2Lookup[i] = Mathf.Log(i / 10f, 2);
            for (int i = 1; i < sqrtLookup.Length; i++)
                sqrtLookup[i] = Mathf.Sqrt(i / 2f);
        }
    }
}
