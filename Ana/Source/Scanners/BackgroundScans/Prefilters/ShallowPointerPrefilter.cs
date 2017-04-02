﻿namespace Ana.Source.Scanners.BackgroundScans.Prefilters
{
    using ActionScheduler;
    using Engine;
    using Engine.OperatingSystems;
    using Engine.Processes;
    using Output;
    using Snapshots;
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using Utils.DataStructures;
    using Utils.Extensions;

    /// <summary>
    /// Class to collect all pointers in the module bases of the target process, and slowly trace pointers from there.
    /// The growth radius will be small, so only a small subset of the processes memory should be explored.
    /// </summary>
    internal class ShallowPointerPrefilter : ScheduledTask, ISnapshotPrefilter
    {
        /// <summary>
        /// The distance from the pointer target where we will keep the addresses in this prefilter.
        /// </summary>
        private const Int32 PointerRadius = 2048;

        /// <summary>
        /// The time between each update cycle.
        /// </summary>
        private const Int32 RescanTime = 4096;

        /// <summary>
        /// Singleton instance of the <see cref="ShallowPointerPrefilter"/> class.
        /// </summary>
        private static Lazy<ISnapshotPrefilter> snapshotPrefilterInstance = new Lazy<ISnapshotPrefilter>(
            () => { return new ShallowPointerPrefilter(); },
            LazyThreadSafetyMode.ExecutionAndPublication);

        /// <summary>
        /// Gets or sets the number of regions processed by this prefilter.
        /// </summary>
        private Int64 processedCount;

        /// <summary>
        /// Prevents a default instance of the <see cref="ShallowPointerPrefilter" /> class from being created.
        /// </summary>
        private ShallowPointerPrefilter() : base("Prefilter", isRepeated: true, trackProgress: true)
        {
            this.PrefilteredSnapshot = new Snapshot();
            this.RegionLock = new Object();
            this.processedCount = 0;

            // TEMP DEBUGGING:
            this.UpdateProgress(1, 1);
            this.IsTaskComplete = this.IsProgressComplete;
        }

        /// <summary>
        /// Gets or sets a lock for accessing snapshot regions.
        /// </summary>
        private Object RegionLock { get; set; }

        /// <summary>
        /// Gets or sets the snapshot constructed by this prefilter.
        /// </summary>
        private Snapshot PrefilteredSnapshot { get; set; }

        /// <summary>
        /// Gets a singleton instance of the <see cref="ShallowPointerPrefilter"/> class.
        /// </summary>
        /// <returns>A singleton instance of the class.</returns>
        public static ISnapshotPrefilter GetInstance()
        {
            return ShallowPointerPrefilter.snapshotPrefilterInstance.Value;
        }

        /// <summary>
        /// Starts the update cycle for this prefilter.
        /// </summary>
        public void BeginPrefilter()
        {
            this.Begin();
        }

        /// <summary>
        /// Gets the snapshot generated by the prefilter.
        /// </summary>
        /// <returns>The snapshot generated by the prefilter.</returns>
        public Snapshot GetPrefilteredSnapshot()
        {
            lock (this.RegionLock)
            {
                return this.PrefilteredSnapshot.Clone();
            }
        }

        /// <summary>
        /// Recieves a process update.
        /// </summary>
        /// <param name="process">The newly selected process.</param>>
        public void Update(NormalizedProcess process)
        {
            lock (this.RegionLock)
            {
                if (this.PrefilteredSnapshot.ByteCount > 0)
                {
                    this.PrefilteredSnapshot = new Snapshot();
                    OutputViewModel.GetInstance().Log(OutputViewModel.LogLevel.Info, "Pointer Prefilter cleared");
                }
            }
        }

        /// <summary>
        /// Starts the prefilter.
        /// </summary>
        protected override void OnBegin()
        {
            this.UpdateInterval = ShallowPointerPrefilter.RescanTime;

            base.OnBegin();
        }

        /// <summary>
        /// Updates the prefilter.
        /// </summary>
        protected override void OnUpdate()
        {
            this.ProcessPages();
            this.UpdateProgress();

            base.OnUpdate();
        }

        protected override void OnEnd()
        {
            base.OnEnd();
        }

        /// <summary>
        /// Processes all pages, computing checksums to determine chunks of virtual pages that have changed.
        /// </summary>
        private void ProcessPages()
        {
            ConcurrentHashSet<IntPtr> foundPointers = new ConcurrentHashSet<IntPtr>();

            lock (this.RegionLock)
            {
                List<SnapshotRegion> regions = new List<SnapshotRegion>();

                // Add static bases
                foreach (NormalizedModule normalizedModule in EngineCore.GetInstance().OperatingSystemAdapter.GetModules())
                {
                    regions.Add(new SnapshotRegion(normalizedModule.BaseAddress, normalizedModule.RegionSize));
                }

                // Add pointer destinations
                foreach (IntPtr pointerDestination in PointerCollector.GetInstance().GetFoundPointerDestinations())
                {
                    regions.Add(new SnapshotRegion(pointerDestination.Subtract(ShallowPointerPrefilter.PointerRadius), ShallowPointerPrefilter.PointerRadius * 2));
                }

                this.PrefilteredSnapshot.AddSnapshotRegions(regions);
                this.processedCount = Math.Max(this.processedCount, this.PrefilteredSnapshot.RegionCount);
            }
        }

        /// <summary>
        /// Updates the progress of how many regions have been processed.
        /// </summary>
        private void UpdateProgress()
        {
            Int32 regionCount = 1;

            lock (this.RegionLock)
            {
                if (this.PrefilteredSnapshot != null)
                {
                    regionCount = Math.Max(regionCount, this.PrefilteredSnapshot.RegionCount);
                }
            }
        }
    }
    //// End class
}
//// End namespace