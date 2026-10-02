// The tests share one headless application and one UI thread; running them side by side only interleaves their windows.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
