using System.Reflection;
using System.Windows.Forms;
using ExpControlsLib;
using NUnit.Framework;
using WindowsApiLib.Shell;

namespace ExpControlsLibTest;

[TestFixture]
[Apartment(ApartmentState.STA)]
public sealed class ExpTreeCharacterizationTests
{
    [SetUp]
    public void SetUp()
    {
        ShellController.Instance.ShellUpdater.AllowUpdates = true;
    }

    [Test]
    public async Task RootReplacementLeavesOnlyTheLatestRoot()
    {
        var rootA = CreateTestDirectory("ExpTreeRootA");
        var rootB = CreateTestDirectory("ExpTreeRootB");

        try
        {
            EnsurePathInHierarchy(rootA);
            EnsurePathInHierarchy(rootB);

            using var tree = new ExpTree();
            tree.Initialize(ShellController.Instance);
            using var form = ShowTree(tree);

            var itemA = FindItem(rootA);
            var itemB = FindItem(rootB);

            tree.Root = itemA;
            await WaitForCondition(() => RootNodeMatches(tree, rootA), "root A to load");

            tree.Root = itemB;
            await WaitForCondition(() => ContainsRootNode(tree, rootB), "root B to load");

            Assert.That(tree.Nodes, Is.Not.Null);
            Assert.That(tree.Nodes!.Count, Is.EqualTo(1), "Replacing the root must replace, not append to, the tree");
            Assert.That(((CShellItem)tree.Nodes[0].Tag!).FullPath, Is.EqualTo(rootB).IgnoreCase);
        }
        finally
        {
            DeleteTestDirectory(rootA);
            DeleteTestDirectory(rootB);
        }
    }

    [Test]
    public async Task CancellingAnInFlightRootLoadDoesNotPublishTheCancelledRoot()
    {
        var rootA = CreateTestDirectory("ExpTreeCancelA");
        var rootB = CreateTestDirectory("ExpTreeCancelB");

        // Give the first load enough work that the second assignment exercises cancellation.
        for (var i = 0; i < 80; i++)
        {
            Directory.CreateDirectory(Path.Combine(rootA, $"Child{i:D2}"));
        }

        try
        {
            EnsurePathInHierarchy(rootA);
            EnsurePathInHierarchy(rootB);

            using var tree = new ExpTree();
            tree.Initialize(ShellController.Instance);
            using var form = ShowTree(tree);

            tree.Root = FindItem(rootA);
            tree.Root = FindItem(rootB);

            await WaitForCondition(() => RootNodeMatches(tree, rootB), "cancelled root load to be replaced by root B", 15000);
            Assert.That(tree.Nodes, Is.Not.Null);
            Assert.That(tree.Nodes!.Count, Is.EqualTo(1));
            Assert.That(((CShellItem)tree.Nodes[0].Tag!).FullPath, Is.EqualTo(rootB).IgnoreCase);
        }
        finally
        {
            DeleteTestDirectory(rootA);
            DeleteTestDirectory(rootB);
        }
    }

    [Test]
    public async Task ExpansionOutsideTheCurrentRootReturnsFalseAndDoesNotChangeSelection()
    {
        var root = CreateTestDirectory("ExpTreeRootBoundary");
        var parent = Directory.GetParent(root)!.FullName;

        try
        {
            EnsurePathInHierarchy(root);

            using var tree = new ExpTree(root);
            tree.Initialize(ShellController.Instance);
            using var form = ShowTree(tree);
            await WaitForCondition(() => tree.Nodes.Count > 0, "tree root to load");

            var rootItem = tree.Root;
            Assert.That(rootItem, Is.Not.Null);

            var expanded = await tree.ExpandANodeAsync(parent);

            Assert.That(expanded, Is.False, "An item outside the configured root is not expandable in this tree");
            Assert.That(tree.SelectedItem, Is.Null.Or.SameAs(rootItem));
        }
        finally
        {
            DeleteTestDirectory(root);
        }
    }

    [Test]
    public async Task ConcurrentExpansionRequestsProduceOneStableSubtree()
    {
        var root = CreateTestDirectory("ExpTreeConcurrentExpand");
        var target = Path.Combine(root, "A", "B", "C");
        Directory.CreateDirectory(target);

        try
        {
            EnsurePathInHierarchy(root);

            using var tree = new ExpTree(root);
            tree.Initialize(ShellController.Instance);
            using var form = ShowTree(tree);
            await WaitForCondition(() => tree.Nodes.Count > 0, "tree root to load");

            var requests = Enumerable.Range(0, 8)
                .Select(_ => tree.ExpandANodeAsync(target))
                .ToArray();
            var results = await Task.WhenAll(requests);

            Assert.That(results, Is.All.True);
            Assert.That(tree.SelectedItem?.FullPath, Is.EqualTo(target).IgnoreCase);
            Assert.That(CountNodesWithPath(tree.Nodes[0], target), Is.EqualTo(1));
        }
        finally
        {
            DeleteTestDirectory(root);
        }
    }

    [Test]
    public async Task FailedBackNavigationDoesNotDiscardTheBackHistoryEntry()
    {
        var root = CreateTestDirectory("ExpTreeFailedBack");
        var pathA = Directory.CreateDirectory(Path.Combine(root, "A")).FullName;
        var pathB = Directory.CreateDirectory(Path.Combine(root, "B")).FullName;

        try
        {
            EnsurePathInHierarchy(root);

            using var tree = new ExpTree(root);
            tree.Initialize(ShellController.Instance);
            using var form = ShowTree(tree);
            await WaitForCondition(() => tree.Nodes.Count > 0, "tree root to load");

            Assert.That(await tree.ExpandANodeAsync(pathA), Is.True);
            Assert.That(await tree.ExpandANodeAsync(pathB), Is.True);

            var itemA = FindItem(pathA);
            ShellController.Instance.ShellUpdater.RaiseUpdateEvent(
                tree.Root!, new ShellItemUpdateEventArgs(itemA, CShItemUpdateType.Deleted));
            await WaitForCondition(
                () => !ContainsDescendant(tree.Nodes[0], pathA),
                "deleted A node to leave the tree");

            await tree.GoBackAsync();

            Assert.That(tree.SelectedItem?.FullPath, Is.EqualTo(pathB).IgnoreCase);
            Assert.That(tree.CanGoBack, Is.True, "A failed navigation must not permanently discard its history entry");

            // The failed target should remain the next back target. A second attempt must
            // retry A, not skip directly to the root that preceded it.
            await tree.GoBackAsync();
            Assert.That(tree.SelectedItem?.FullPath, Is.EqualTo(pathB).IgnoreCase);
        }
        finally
        {
            DeleteTestDirectory(root);
        }
    }

    [Test]
    public async Task HandleRecreationAndRepeatedDisposeRemainSafe()
    {
        var root = CreateTestDirectory("ExpTreeHandleLifecycle");

        try
        {
            EnsurePathInHierarchy(root);

            var tree = new ExpTree(root);
            tree.Initialize(ShellController.Instance);
            using var form = ShowTree(tree);
            await WaitForCondition(() => tree.Nodes.Count > 0, "tree root to load");

            tree.AllowDrop = true;
            var childTreeView = tree.Controls.OfType<TreeView>().Single();
            var recreateHandle = typeof(Control).GetMethod(
                "RecreateHandle", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(recreateHandle, Is.Not.Null, "WinForms must expose the protected handle recreation method");

            Assert.DoesNotThrow(() => recreateHandle!.Invoke(childTreeView, null));
            Application.DoEvents();

            Assert.That(tree.AllowDrop, Is.True);
            Assert.DoesNotThrow(() => tree.Dispose());
            Assert.DoesNotThrow(() => tree.Dispose());
        }
        finally
        {
            DeleteTestDirectory(root);
        }
    }

    private static Form ShowTree(ExpTree tree)
    {
        var form = new Form();
        form.Controls.Add(tree);
        form.Show();
        return form;
    }

    private static string CreateTestDirectory(string prefix)
    {
        var path = Path.Combine(Path.GetTempPath(), $"{prefix}_{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteTestDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    private static void EnsurePathInHierarchy(string path)
    {
        var parentPath = Path.GetDirectoryName(path);
        if (parentPath is null) return;

        var parent = ShellController.Instance.HierachyManager.FindAndAllowExpansion(parentPath);
        parent?.LoadFolderContents(false, true);
    }

    private static CShellItem FindItem(string path)
    {
        var item = ShellController.Instance.HierachyManager.FindAndAllowExpansion(path);
        Assert.That(item, Is.Not.Null, $"Expected Shell hierarchy item for '{path}'");
        return item!;
    }

    private static bool PathsEqual(string? actual, string expected) =>
        actual is not null && actual.Equals(expected, StringComparison.OrdinalIgnoreCase);

    private static bool RootNodeMatches(ExpTree tree, string expectedPath)
    {
        return tree.Nodes is { Count: 1 }
            && tree.Nodes[0].Tag is CShellItem item
            && PathsEqual(item.FullPath, expectedPath);
    }

    private static bool ContainsRootNode(ExpTree tree, string expectedPath)
    {
        return tree.Nodes is not null
            && tree.Nodes.Cast<TreeNode>().Any(node =>
                node.Tag is CShellItem item && PathsEqual(item.FullPath, expectedPath));
    }

    private static int CountNodesWithPath(TreeNode root, string path)
    {
        var count = 0;
        foreach (TreeNode node in root.Nodes)
        {
            if (node.Tag is CShellItem item && PathsEqual(item.FullPath, path)) count++;
            count += CountNodesWithPath(node, path);
        }

        return count;
    }

    private static bool ContainsDescendant(TreeNode root, string path)
    {
        return root.Nodes.Cast<TreeNode>().Any(node =>
            (node.Tag is CShellItem item && PathsEqual(item.FullPath, path))
            || ContainsDescendant(node, path));
    }

    private static async Task WaitForCondition(Func<bool> condition, string message, int timeoutMs = 10000)
    {
        var start = DateTime.UtcNow;
        while (!condition())
        {
            if ((DateTime.UtcNow - start).TotalMilliseconds > timeoutMs)
            {
                Assert.Fail($"Timeout waiting for: {message}");
            }

            await Task.Delay(10);
            Application.DoEvents();
        }
    }
}
