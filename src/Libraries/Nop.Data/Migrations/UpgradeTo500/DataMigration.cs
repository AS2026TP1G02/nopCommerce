using FluentMigrator;
using Nop.Core.Domain.Logging;

namespace Nop.Data.Migrations.UpgradeTo500;

[NopUpdateMigration("2026-04-24 00:00:00", "5.00", UpdateMigrationType.Data)]
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

        //#1832
        if (!activityLogTypeTable.Any(alt => string.Compare(alt.SystemKeyword, "AddNewContactFormAttribute", StringComparison.InvariantCultureIgnoreCase) == 0))
        {
            _dataProvider.InsertEntity(
                new ActivityLogType
                {
                    SystemKeyword = "AddNewContactFormAttribute",
                    Enabled = true,
                    Name = "Add a new contact form attribute"
                }
            );
        }
       
        if (!activityLogTypeTable.Any(alt => string.Compare(alt.SystemKeyword, "EditContactFormAttribute", StringComparison.InvariantCultureIgnoreCase) == 0))
        {
            _dataProvider.InsertEntity(
                new ActivityLogType
                {
                    SystemKeyword = "EditContactFormAttribute",
                    Enabled = true,
                    Name = "Edit a contact form attribute"
                }
            );
        }

        if (!activityLogTypeTable.Any(alt => string.Compare(alt.SystemKeyword, "DeleteContactFormAttribute", StringComparison.InvariantCultureIgnoreCase) == 0))
        {
            _dataProvider.InsertEntity(
                new ActivityLogType
                {
                    SystemKeyword = "DeleteContactFormAttribute",
                    Enabled = true,
                    Name = "Delete a contact form attribute"
                }
            );
        }

        if (!activityLogTypeTable.Any(alt => string.Compare(alt.SystemKeyword, "AddNewContactFormAttributeValue", StringComparison.InvariantCultureIgnoreCase) == 0))
        {
            _dataProvider.InsertEntity(
                new ActivityLogType
                {
                    SystemKeyword = "AddNewContactFormAttributeValue",
                    Enabled = true,
                    Name = "Add a new contact form attribute value"
                }
            );
        }

        if (!activityLogTypeTable.Any(alt => string.Compare(alt.SystemKeyword, "EditContactFormAttributeValue", StringComparison.InvariantCultureIgnoreCase) == 0))
        {
            _dataProvider.InsertEntity(
                new ActivityLogType
                {
                    SystemKeyword = "EditContactFormAttributeValue",
                    Enabled = true,
                    Name = "Edit a contact form attribute value"
                }
            );
        }

        if (!activityLogTypeTable.Any(alt => string.Compare(alt.SystemKeyword, "DeleteContactFormAttributeValue", StringComparison.InvariantCultureIgnoreCase) == 0))
        {
            _dataProvider.InsertEntity(
                new ActivityLogType
                {
                    SystemKeyword = "DeleteContactFormAttributeValue",
                    Enabled = true,
                    Name = "Delete a contact form attribute value"
                }
            );
        }
    }

    public override void Down()
    {
        //add the downgrade logic if necessary 
    }
}