using ExpTreeLib;
using System.Windows.Forms;

namespace WindowsApiLibTest
{
    [TestClass]
    public sealed class Test1
    {
        [TestMethod]
        public void TestExpTreeNodesProperty()
        {
            var expTree = new ExpTree();
            Assert.IsNotNull(expTree.Nodes, "Nodes property should not be null.");
            Assert.IsInstanceOfType(expTree.Nodes, typeof(TreeNodeCollection), "Nodes property should be of type TreeNodeCollection.");
        }

        [TestMethod]
        public void TestExpListItemsProperty()
        {
            var expList = new ExpList();
            Assert.IsNotNull(expList.Items, "Items property should not be null.");
            Assert.IsInstanceOfType(expList.Items, typeof(ListView.ListViewItemCollection), "Items property should be of type ListViewItemCollection.");
        }
    }
}
