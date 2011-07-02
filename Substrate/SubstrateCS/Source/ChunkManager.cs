﻿using System;
using System.Collections;
using System.Collections.Generic;
using Substrate.Core;

namespace Substrate
{
    public class ChunkManager : IChunkManager, IEnumerable<ChunkRef>
    {
        public const int REGION_XLEN = 32;
        public const int REGION_ZLEN = 32;

        public const int REGION_XLOG = 5;
        public const int REGION_ZLOG = 5;

        public const int REGION_XMASK = 0x1F;
        public const int REGION_ZMASK = 0x1F;

        protected RegionManager _regionMan;

        //protected Dictionary<RegionKey, Region> _cache;
        //protected Dictionary<RegionKey, Region> _dirty;

        protected ChunkCache _cache;

        public ChunkManager (RegionManager rm, ChunkCache cache)
        {
            _regionMan = rm;
            _cache = cache;
            //_cache = new Dictionary<RegionKey, Region>();
            //_dirty = new Dictionary<RegionKey, Region>();
        }

        public ChunkManager (ChunkManager cm)
        {
            _regionMan = cm._regionMan;
            _cache = cm._cache;
            //_cache = new Dictionary<RegionKey, Region>();
            //_dirty = new Dictionary<RegionKey, Region>();
        }

        public int ChunkGlobalX (int cx)
        {
            return cx;
        }

        public int ChunkGlobalZ (int cz)
        {
            return cz;
        }

        public int ChunkLocalX (int cx)
        {
            return cx & REGION_XMASK;
        }

        public int ChunkLocalZ (int cz)
        {
            return cz & REGION_ZMASK;
        }

        public Chunk GetChunk (int cx, int cz)
        {
            Region r = GetRegion(cx, cz);
            if (r == null) {
                return null;
            }

            return r.GetChunk(cx & REGION_XMASK, cz & REGION_ZMASK);
        }

        public ChunkRef GetChunkRef (int cx, int cz)
        {
            Region r = GetRegion(cx, cz);
            if (r == null) {
                return null;
            }

            return r.GetChunkRef(cx & REGION_XMASK, cz & REGION_ZMASK);
        }

        public bool ChunkExists (int cx, int cz)
        {
            Region r = GetRegion(cx, cz);
            if (r == null) {
                return false;
            }

            return r.ChunkExists(cx & REGION_XMASK, cz & REGION_ZMASK);
        }

        public ChunkRef CreateChunk (int cx, int cz)
        {
            Region r = GetRegion(cx, cz);
            if (r == null) {
                int rx = cx >> REGION_XLOG;
                int rz = cz >> REGION_ZLOG;
                r = _regionMan.CreateRegion(rx, rz);
            }

            return r.CreateChunk(cx & REGION_XMASK, cz & REGION_ZMASK);
        }

        public ChunkRef CopyChunk (int src_cx, int src_cz, int dst_cx, int dst_cz)
        {
            Region src_r = GetRegion(src_cx, src_cz);
            if (src_r == null) {
                return null;
            }

            Region dst_r = GetRegion(dst_cx, dst_cz);
            if (dst_r == null) {
                int rx = dst_cx >> REGION_XLOG;
                int rz = dst_cz >> REGION_ZLOG;
                dst_r = _regionMan.CreateRegion(rx, rz);
            }

            Chunk c = src_r.GetChunk(src_cx & REGION_XMASK, src_cz & REGION_ZMASK).Copy();
            c.SetLocation(dst_cx, dst_cz);

            dst_r.SaveChunk(c);

            return dst_r.GetChunkRef(dst_cx & REGION_XMASK, dst_cz & REGION_ZMASK);
        }

        public ChunkRef SetChunk (int cx, int cz, Chunk chunk)
        {
            Region r = GetRegion(cx, cz);
            if (r == null) {
                int rx = cx >> REGION_XLOG;
                int rz = cz >> REGION_ZLOG;
                r = _regionMan.CreateRegion(rx, rz);
            }

            chunk.SetLocation(cx, cz);
            r.SaveChunk(chunk);

            return r.GetChunkRef(cx & REGION_XMASK, cz & REGION_ZMASK);
        }

        public int Save ()
        {
            _cache.SyncDirty();

            int saved = 0;
            IEnumerator<ChunkRef> en = _cache.GetDirtyEnumerator();
            while (en.MoveNext()) {
                ChunkRef chunk = en.Current;

                Region r = GetRegion(chunk.X, chunk.Z);
                if (r == null) {
                    continue;
                }

                chunk.Save(r.GetChunkOutStream(chunk.LocalX, chunk.LocalZ));
                saved++;
            }

            _cache.ClearDirty();
            return saved;
        }

        public bool SaveChunk (Chunk chunk)
        {
            Region r = GetRegion(chunk.X, chunk.Z);
            if (r == null) {
                return false;
            }

            return r.SaveChunk(chunk);
        }

        public bool DeleteChunk (int cx, int cz)
        {
            Region r = GetRegion(cx, cz);
            if (r == null) {
                return false;
            }

            if (!r.DeleteChunk(cx & REGION_XMASK, cz & REGION_ZMASK)) {
                return false;
            }

            if (r.ChunkCount() == 0) {
                _regionMan.DeleteRegion(r.X, r.Z);
            }

            return true;
        }

        public void RelightDirtyChunks ()
        {
            //List<ChunkRef> dirty = new List<ChunkRef>();
            Dictionary<ChunkKey, ChunkRef> dirty = new Dictionary<ChunkKey, ChunkRef>();

            _cache.SyncDirty();

            IEnumerator<ChunkRef> en = _cache.GetDirtyEnumerator();
            while (en.MoveNext()) {
                ChunkKey key = new ChunkKey(en.Current.X, en.Current.Z);
                dirty[key] = en.Current;
            }

            foreach (ChunkRef chunk in dirty.Values) {
                chunk.Blocks.ResetBlockLight();
                chunk.Blocks.ResetSkyLight();
            }

            foreach (ChunkRef chunk in dirty.Values) {
                chunk.Blocks.RebuildBlockLight();
                chunk.Blocks.RebuildSkyLight();
            }

            foreach (ChunkRef chunk in dirty.Values) {  
                if (!dirty.ContainsKey(new ChunkKey(chunk.X, chunk.Z - 1))) {
                    ChunkRef east = chunk.GetEastNeighbor();
                    chunk.Blocks.StitchBlockLight(east.Blocks, BlockCollectionEdge.EAST);
                    chunk.Blocks.StitchSkyLight(east.Blocks, BlockCollectionEdge.EAST);
                }

                if (!dirty.ContainsKey(new ChunkKey(chunk.X, chunk.Z + 1))) {
                    ChunkRef west = chunk.GetWestNeighbor();
                    chunk.Blocks.StitchBlockLight(west.Blocks, BlockCollectionEdge.WEST);
                    chunk.Blocks.StitchSkyLight(west.Blocks, BlockCollectionEdge.WEST);
                }

                if (!dirty.ContainsKey(new ChunkKey(chunk.X - 1, chunk.Z))) {
                    ChunkRef north = chunk.GetNorthNeighbor();
                    chunk.Blocks.StitchBlockLight(north.Blocks, BlockCollectionEdge.NORTH);
                    chunk.Blocks.StitchSkyLight(north.Blocks, BlockCollectionEdge.NORTH);
                }

                if (!dirty.ContainsKey(new ChunkKey(chunk.X + 1, chunk.Z))) {
                    ChunkRef south = chunk.GetSouthNeighbor();
                    chunk.Blocks.StitchBlockLight(south.Blocks, BlockCollectionEdge.SOUTH);
                    chunk.Blocks.StitchSkyLight(south.Blocks, BlockCollectionEdge.SOUTH);
                }
            }
        }

        public RegionManager GetRegionManager ()
        {
            return _regionMan;
        }

        public ChunkRef GetChunkRefInRegion (Region r, int lcx, int lcz)
        {
            int cx = r.X * REGION_XLEN + lcx;
            int cz = r.Z * REGION_ZLEN + lcz;
            return GetChunkRef(cx, cz);
        }

        protected Region GetRegion (int cx, int cz)
        {
            cx >>= REGION_XLOG;
            cz >>= REGION_ZLOG;
            return _regionMan.GetRegion(cx, cz);
        }


        #region IEnumerable<ChunkRef> Members

        public IEnumerator<ChunkRef> GetEnumerator ()
        {
            return new Enumerator(this);
        }

        #endregion


        #region IEnumerable Members

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator ()
        {
            return new Enumerator(this);
        }

        #endregion


        private class Enumerator : IEnumerator<ChunkRef>
        {
            private ChunkManager _cm;

            private IEnumerator<Region> _enum;
            private Region _region;
            private ChunkRef _chunk;

            private int _x = 0;
            private int _z = -1;

            public Enumerator (ChunkManager cm)
            {
                _cm = cm;
                _enum = _cm.GetRegionManager().GetEnumerator();
                _enum.MoveNext();
                _region = _enum.Current;
            }

            public virtual bool MoveNext ()
            {
                if (_enum == null) {
                    return MoveNextInRegion();
                }
                else {
                    while (true) {
                        if (_x >= ChunkManager.REGION_XLEN) {
                            if (!_enum.MoveNext()) {
                                return false;
                            }
                            _x = 0;
                            _z = -1;
                            _region = _enum.Current;
                        }
                        if (MoveNextInRegion()) {
                            _chunk = _region.GetChunkRef(_x, _z);
                            return true;
                        }
                    }
                }
            }

            protected bool MoveNextInRegion ()
            {
                for (; _x < ChunkManager.REGION_XLEN; _x++) {
                    for (_z++; _z < ChunkManager.REGION_ZLEN; _z++) {
                        if (_region.ChunkExists(_x, _z)) {
                            goto FoundNext;
                        }
                    }
                    _z = -1;
                }

            FoundNext:

                return (_x < ChunkManager.REGION_XLEN);
            }

            public void Reset ()
            {
                if (_enum != null) {
                    _enum.Reset();
                    _enum.MoveNext();
                    _region = _enum.Current;
                }
                _x = 0;
                _z = -1;
            }

            void IDisposable.Dispose () { }

            object IEnumerator.Current
            {
                get
                {
                    return Current;
                }
            }

            ChunkRef IEnumerator<ChunkRef>.Current
            {
                get
                {
                    return Current;
                }
            }

            public ChunkRef Current
            {
                get
                {
                    if (_x >= ChunkManager.REGION_XLEN) {
                        throw new InvalidOperationException();
                    }
                    return _chunk;
                }
            }
        }
    }

    public class MissingChunkException : Exception
    {

    }
}
