using FluentMigrator;
using Nop.Core.Domain.Logging;

namespace Nop.Data.Migrations.UpgradeTo500;

[NopUpdateMigration("2025-03-25 00:00:00", "5.00", UpdateMigrationType.Data)]
public class DataMigration : Migration
{
    private readonly INopDataProvider _dataProvider;

    public DataMigration(INopDataProvider dataProvider)
    {
        _dataProvider = dataProvider;
    }

    /// <summary>
    /// Collect the UP migration expressions
    /// </summary>
    public override void Up()
    {
        var activityLogTypeTable = _dataProvider.GetTable<ActivityLogType>();

        //#8098
        if (!activityLogTypeTable.Any(alt => string.Compare(alt.SystemKeyword, "AddNewPriceList", StringComparison.InvariantCultureIgnoreCase) == 0))
        {
            _dataProvider.InsertEntity(
                new ActivityLogType
                {
                    SystemKeyword = "AddNewPriceList",
                    Enabled = true,
                    Name = "Add a new price list"
                }
            );
        }

        if (!activityLogTypeTable.Any(alt => string.Compare(alt.SystemKeyword, "DeletePriceList", StringComparison.InvariantCultureIgnoreCase) == 0))
        {
            _dataProvider.InsertEntity(
                new ActivityLogType
                {
                    SystemKeyword = "DeletePriceList",
                    Enabled = true,
                    Name = "Delete a price list"
                }
            );
        }

        if (!activityLogTypeTable.Any(alt => string.Compare(alt.SystemKeyword, "EditPriceList", StringComparison.InvariantCultureIgnoreCase) == 0))
        {
            _dataProvider.InsertEntity(
                new ActivityLogType
                {
                    SystemKeyword = "EditPriceList",
                    Enabled = true,
                    Name = "Edit a price list"
                }
            );
        }

        if (!activityLogTypeTable.Any(alt => string.Compare(alt.SystemKeyword, "ExportPriceLists", StringComparison.InvariantCultureIgnoreCase) == 0))
        {
            _dataProvider.InsertEntity(
                new ActivityLogType
                {
                    SystemKeyword = "ExportPriceLists",
                    Enabled = true,
                    Name = "Export price lists"
                }
            );
        }

        if (!activityLogTypeTable.Any(alt => string.Compare(alt.SystemKeyword, "ImportPriceLists", StringComparison.InvariantCultureIgnoreCase) == 0))
        {
            _dataProvider.InsertEntity(
                new ActivityLogType
                {
                    SystemKeyword = "ImportPriceLists",
                    Enabled = true,
                    Name = "Import price lists"
                }
            );
        }
    }

    public override void Down()
    {
        //add the downgrade logic if necessary 
    }
}
