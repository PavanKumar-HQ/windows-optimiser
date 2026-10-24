using System.Threading.Tasks;

namespace Squirrel.Core.Actions
{
    public interface ISystemAction
    {
        string Id { get; }
        string Title { get; }
        string Description { get; }
        ActionRiskLevel RiskLevel { get; }
        bool IsReversible { get; }
        bool RequiresAdmin { get; }

        /// <summary>
        /// Executes the action only if explicit user confirmation is passed.
        /// </summary>
        Task<ActionResult> ExecuteAsync(bool userConfirmed);

        /// <summary>
        /// Reverts the action if reversible.
        /// </summary>
        Task<ActionResult> RollbackAsync();
    }
}
