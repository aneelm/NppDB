using NppDB.Comm;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static System.Net.Mime.MediaTypeNames;
using System.Windows.Forms;
using System.Data.OleDb;
using System.Data;
using System.Data.Odbc;
using System.Collections;
using Npgsql;

namespace NppDB.PostgreSQL
{
    public class PostgreSQLTableGroup : TreeNode, IRefreshable, IMenuProvider
    {
        public PostgreSQLTableGroup()
        {
            Query = "SELECT table_name FROM information_schema.tables WHERE table_schema='{0}' AND table_type='BASE TABLE' ORDER BY table_name";
            Text = "Tables";
            SelectedImageKey = ImageKey = "Group";
        }

        protected string Query { get; set; }

        protected virtual TreeNode CreateTreeNode(NpgsqlDataReader reader)
        {
            return new PostgreSQLTable
            {
                Text = reader["table_name"].ToString()
            };
        }

        public void Refresh()
        {
            var conn = (PostgreSQLConnect)Parent.Parent;
            using (var cnn = conn.GetConnection())
            {
                TreeView.Cursor = Cursors.WaitCursor;
                TreeView.Enabled = false;
                try
                {
                    cnn.Open();
                    Nodes.Clear();
                    using (NpgsqlCommand command = new NpgsqlCommand(String.Format(Query, Parent.Text), cnn))
                    {
                        using (NpgsqlDataReader reader = command.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                Nodes.Add(CreateTreeNode(reader));
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message, "Exception");
                }
                finally
                {
                    TreeView.Enabled = true;
                    TreeView.Cursor = null;
                }
            }
        }

        public ContextMenuStrip GetMenu()
        {
            var menuList = new ContextMenuStrip { ShowImageMargin = false };
            menuList.Items.Add(new ToolStripButton("Refresh", null, (s, e) => { Refresh(); }));

            return menuList;
        }
    }
}
