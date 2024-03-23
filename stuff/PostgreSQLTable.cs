using NppDB.Comm;
using System;
using System.Collections.Generic;
using System.Data.Odbc;
using System.Windows.Forms;
using System.Text.RegularExpressions;
using System.Linq;
using Npgsql;

namespace NppDB.PostgreSQL
{
    public class PostgreSQLTable : TreeNode, IRefreshable, IMenuProvider
    {
        public string Definition { get; set; }
        protected string TypeName { get; set; } = "TABLE";
        public PostgreSQLTable()
        {
            SelectedImageKey = ImageKey = "Table";
        }

        public void Refresh()
        {
            var conn = GetDBConnect();
            using (var cnn = conn.GetConnection())
            {
                TreeView.Enabled = false;
                TreeView.Cursor = Cursors.WaitCursor;
                try
                {
                    cnn.Open();
                    Nodes.Clear();

                    var columns = new List<PostgreSQLColumnInfo>();

                    var primaryKeyColumnNames = CollectPrimaryKeys(cnn, ref columns);
                    var foreignKeyColumnNames = CollectForeignKeys(cnn, ref columns);
                    var indexedColumnNames = CollectIndices(cnn, ref columns);

                    var columnCount = CollectColumns(cnn, ref columns, primaryKeyColumnNames, foreignKeyColumnNames, indexedColumnNames);
                    if (columnCount == 0) return;

                    var maxLength = columns.Max(c => c.ColumnName.Length);
                    columns.ForEach(c => c.AdjustColumnNameFixedWidth(maxLength));
                    Nodes.AddRange(columns.ToArray<TreeNode>());
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

        private int CollectColumns(NpgsqlConnection connection, ref List<PostgreSQLColumnInfo> columns,
            in List<string> primaryKeyColumnNames,
            in List<string> foreignKeyColumnNames,
            in List<string> indexedColumnNames
            )
        {
            var count = 0;
            String query = "SELECT * FROM information_schema.columns WHERE table_schema = '{0}' AND table_name = '{1}' ORDER BY ordinal_position;";
            using (NpgsqlCommand command = new NpgsqlCommand(String.Format(query, getSchemaName(), Text), connection))
            {
                using (NpgsqlDataReader reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        var columnName = reader["column_name"].ToString();
                        var dataTypeName = GetDataTypeName(reader);

                        var options = 0;

                        if (reader["is_nullable"].ToString() == "YES") options += 1;
                        if (indexedColumnNames.Contains(columnName)) options += 10;
                        if (primaryKeyColumnNames.Contains(columnName)) options += 100;
                        if (foreignKeyColumnNames.Contains(columnName)) options += 1000;

                        columns.Insert(count++, new PostgreSQLColumnInfo(columnName, dataTypeName, 0, options));
                    }
                }
            }
            return count;
        }

        private string GetDataTypeName(NpgsqlDataReader reader)
        {
            var dataType = reader["data_type"].ToString();
            var charMaxLen = reader["character_maximum_length"].ToString();
            var numericPrecision = reader["numeric_precision"].ToString();
            var numericScale = reader["numeric_scale"].ToString();
            var columnDefault = reader["column_default"].ToString();
            if (!String.IsNullOrEmpty(charMaxLen))
            {
                dataType += $"({charMaxLen})";
            } 
            else if (!String.IsNullOrEmpty(numericPrecision) && dataType.Equals("numeric", StringComparison.OrdinalIgnoreCase))
            {
                dataType += $"({numericPrecision}";
                if (!String.IsNullOrEmpty(numericScale))
                {
                    dataType += $",{numericScale}";
                }
                dataType += ")";
            }
            if (!String.IsNullOrEmpty(columnDefault))
            {
                dataType += $" => {columnDefault}";
            }
            return dataType.ToUpper();
        }

        private List<string> CollectPrimaryKeys(NpgsqlConnection connection, ref List<PostgreSQLColumnInfo> columns)
        {
            var query = "SELECT distinct conname as constraint_name " +
                ", pg_get_constraintdef(oid) as constraint_definition " +
                "FROM pg_constraint " +
                "JOIN pg_attribute a ON a.attrelid = conrelid " +
                "WHERE contype IN('p') " +
                "AND connamespace = '{0}'::regnamespace " +
                "AND conrelid = '{1}'::regclass";

            var names = new List<string>();
            using (NpgsqlCommand command = new NpgsqlCommand(String.Format(query, getSchemaName(), Text), connection))
            {
                using (NpgsqlDataReader reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        var primaryKeyName = reader["constraint_name"].ToString();
                        var primaryKeyType = reader["constraint_definition"].ToString();
                        Match primaryKeyTargetMatch = Regex.Match(primaryKeyType, @"PRIMARY KEY \((?s)(.*)\)", RegexOptions.IgnoreCase);
                        if (primaryKeyTargetMatch.Success && primaryKeyTargetMatch.Groups.Count > 1)
                        {
                            names.Add(primaryKeyTargetMatch.Groups[1].ToString());
                        }
                        columns.Add(new PostgreSQLColumnInfo(primaryKeyName, primaryKeyType, 1, 0));
                    }
                }
            }
            return names;
        }

        private List<string> CollectForeignKeys(NpgsqlConnection connection, ref List<PostgreSQLColumnInfo> columns)
        {
            var query = "SELECT distinct conname as constraint_name " +
                ", pg_get_constraintdef(oid) as constraint_definition " +
                "FROM pg_constraint " +
                "JOIN pg_attribute a ON a.attrelid = conrelid " +
                "WHERE contype IN('f') " +
                "AND connamespace = '{0}'::regnamespace " +
                "AND conrelid = '{1}'::regclass"; ;

            var names = new List<string>();

            using (NpgsqlCommand command = new NpgsqlCommand(String.Format(query, getSchemaName(), Text), connection))
            {
                using (NpgsqlDataReader reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        var foreignKeyName = reader["constraint_name"].ToString();
                        var foreignKeyDef = reader["constraint_definition"].ToString();

                        var foreignKeyDefFormatted = reader["constraint_definition"].ToString();
                        Match fkColumnNameMatch = Regex.Match(foreignKeyDef, @"FOREIGN KEY \((?s)(.*)\) REFERENCES", RegexOptions.IgnoreCase);
                        Match fkTargetMatch = Regex.Match(foreignKeyDef, @"REFERENCES (?s)(.*)", RegexOptions.IgnoreCase);
                        if (fkColumnNameMatch.Success && fkColumnNameMatch.Groups.Count > 1 && fkTargetMatch.Success && fkTargetMatch.Groups.Count > 1) 
                        {
                            foreignKeyDefFormatted = $"({fkColumnNameMatch.Groups[1]}) -> {fkTargetMatch.Groups[1]}";
                            names.Add(fkColumnNameMatch.Groups[1].ToString());
                        }
                        columns.Add(new PostgreSQLColumnInfo(foreignKeyName, foreignKeyDefFormatted, 2, 0));
                    }
                }
            }
            return names;
        }

        private List<string> CollectIndices(NpgsqlConnection connection, ref List<PostgreSQLColumnInfo> columns)
        {
            var query = "select * from pg_indexes where schemaname = '{0}' and tablename = '{1}';";

            var names = new List<string>();
            using (NpgsqlCommand command = new NpgsqlCommand(String.Format(query, getSchemaName(), Text), connection))
            {
                using (NpgsqlDataReader reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        var indexName = reader["indexname"].ToString();
                        string indexDef = reader["indexdef"].ToString();

                        var indexDefFormatted = reader["indexdef"].ToString();
                        Match indexDefMatch = Regex.Match(indexDef, @"btree \((?s)(.*)\)", RegexOptions.IgnoreCase);
                        if (indexDefMatch.Success && indexDefMatch.Groups.Count > 1)
                        {
                            string indexColumns = indexDefMatch.Groups[1].ToString();
                            foreach (string column in indexColumns.Split(','))
                            {
                                names.Add(column.Trim());
                            }
                            indexDefFormatted = $"({indexColumns})";
                        }

                        columns.Add(new PostgreSQLColumnInfo(indexName, indexDefFormatted, indexDef?.IndexOf("unique", StringComparison.OrdinalIgnoreCase) >= 0 ? 4 : 3, 0));
                    }
                }
            }
            return names;
        }

        public virtual ContextMenuStrip GetMenu()
        {
            var menuList = new ContextMenuStrip { ShowImageMargin = false };
            var connect = GetDBConnect();
            menuList.Items.Add(new ToolStripButton("Refresh", null, (s, e) => { Refresh(); }));
            if (connect?.CommandHost == null) return menuList;
            menuList.Items.Add(new ToolStripSeparator());

            var host = connect.CommandHost;
            menuList.Items.Add(new ToolStripButton($"SELECT * FROM {Text}", null, (s, e) =>
            {
                host.Execute(NppDBCommandType.NewFile, null);
                var id = host.Execute(NppDBCommandType.GetActivatedBufferID, null);
                var query = $"SELECT * FROM {getSchemaName()}.{Text};";
                host.Execute(NppDBCommandType.AppendToCurrentView, new object[] { query });
                host.Execute(NppDBCommandType.CreateResultView, new[] { id, connect, connect.CreateSQLExecutor() });
                host.Execute(NppDBCommandType.ExecuteSQL, new[] { id, query });
            }));
            menuList.Items.Add(new ToolStripButton($"DROP {TypeName.ToUpper()}", null, (s, e) =>
            {
                var id = host.Execute(NppDBCommandType.GetActivatedBufferID, null);
                var query = $"DROP {TypeName} {getSchemaName()}.{Text};";
                host.Execute(NppDBCommandType.ExecuteSQL, new[] { id, query });
            }));
            return menuList;
        }

        private PostgreSQLConnect GetDBConnect()
        {
            var connect = Parent.Parent.Parent as PostgreSQLConnect;
            return connect;
        }

        private string getSchemaName()
        {
            PostgreSQLSchema schema = Parent.Parent as PostgreSQLSchema;
            return schema.Schema;
        }
    }
}
