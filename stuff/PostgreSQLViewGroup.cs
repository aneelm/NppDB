using NppDB.Comm;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Odbc;
using System.Data.OleDb;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Npgsql;

namespace NppDB.PostgreSQL
{
    internal class PostgreSQLViewGroup : PostgreSQLTableGroup
    {
        public PostgreSQLViewGroup()
        {
            Query = "SELECT table_name FROM information_schema.tables WHERE table_schema='{0}' and table_type='VIEW' ORDER BY table_name";
            Text = "Views";
        }

        protected override TreeNode CreateTreeNode(NpgsqlDataReader reader)
        {
            return new PostgreSQLView
            {
                Text = reader["table_name"].ToString(),
                //Definition = dataRow["view_definition"].ToString(),
            };
        }
    }
}
