using Sentinel.AgentWorker.Domain.Executions;
using Sentinel.Contracts.Primitives.Identifiers;
using Shouldly;

namespace Sentinel.AgentWorker.Tests.Domain;

/// <summary>An investigator run's lifecycle: what it spends, what it records and how it finishes.</summary>
public sealed class AgentExecutionTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 29, 9, 30, 0, TimeSpan.Zero);

    private static AgentExecution Started() => AgentExecution.Start(
        AgentExecutionId.New(),
        TenantId.From("acme"),
        EnvironmentId.From("reference"),
        IncidentId.New(),
        InvestigationId.New(),
        "model",
        shadow: false,
        new ExecutionModel("sentinel-test-double", "deterministic-analyst", "1", "investigator-v3"),
        Now);

    private static ToolCallRecord Call(string id) => new()
    {
        ToolCallId = id,
        ToolName = "metric_range",
        Arguments = "{}",
        Result = "[]",
        StartedAt = Now,
        Duration = TimeSpan.FromMilliseconds(10)
    };

    [Fact]
    public void Model_calls_count_against_the_token_budget_and_the_call_that_exceeds_it_is_refused()
    {
        AgentExecution execution = Started();

        execution.RecordModelCall(600, 200, tokenBudget: 1000).IsSuccess.ShouldBeTrue();
        CleanArchitecture.BuildingBlocks.Result over = execution.RecordModelCall(150, 100, tokenBudget: 1000);

        over.Error.ShouldBe(AgentExecutionErrors.TokenBudgetExhausted);
        over.Error.Description.ShouldBe("The model token budget was exhausted.");
        execution.ModelCalls.ShouldBe(2);
        execution.InputTokens.ShouldBe(750);
        execution.OutputTokens.ShouldBe(300);
    }

    [Fact]
    public void One_rejected_output_may_be_repaired_and_the_second_is_refused()
    {
        AgentExecution execution = Started();

        execution.RecordSchemaFailure().IsSuccess.ShouldBeTrue();
        execution.RecordSchemaFailure().Error.ShouldBe(AgentExecutionErrors.InvalidOutputAfterRepair);
        execution.SchemaFailures.ShouldBe(2);
    }

    [Fact]
    public void The_tool_budget_bounds_how_many_calls_are_recorded_before_the_model_must_conclude()
    {
        AgentExecution execution = Started();
        execution.RecordToolCall(Call("call-1"));

        execution.HasToolBudget(2).ShouldBeTrue();
        execution.RecordToolCall(Call("call-2"));
        execution.HasToolBudget(2).ShouldBeFalse();
        AgentExecutionErrors.ToolBudgetExhausted(2).Description.ShouldBe("The tool budget of 2 calls is exhausted; conclude from what you have.");
    }

    [Fact]
    public void A_finished_run_records_its_outcome_and_becomes_the_audit_record()
    {
        AgentExecution execution = Started();
        execution.RecordModelCall(100, 50, tokenBudget: 1000);
        execution.RecordToolCall(Call("call-1"));

        execution.Abstain("No conclusion within 8 model turns.", Now.AddSeconds(30)).IsSuccess.ShouldBeTrue();
        AgentExecutionRecord record = execution.ToRecord("{}");

        record.Result.ShouldBe(ExecutionResult.Abstained);
        record.AbstentionReason.ShouldBe("No conclusion within 8 model turns.");
        record.ToolCalls.ShouldHaveSingleItem().ToolCallId.ShouldBe("call-1");
        record.InputTokens.ShouldBe(100);
        record.PromptVersion.ShouldBe("investigator-v3");
        record.StartedAt.ShouldBe(Now);
        record.CompletedAt.ShouldBe(Now.AddSeconds(30));
    }

    [Fact]
    public void A_run_finishes_once_and_then_records_nothing_more()
    {
        AgentExecution execution = Started();
        execution.Complete(Now).IsSuccess.ShouldBeTrue();

        execution.Fail("late failure", Now).Error.ShouldBe(AgentExecutionErrors.Finished);
        execution.RecordToolCall(Call("late")).Error.ShouldBe(AgentExecutionErrors.Finished);
        execution.RecordModelCall(1, 1, 1000).Error.ShouldBe(AgentExecutionErrors.Finished);
        execution.State.ShouldBe(ExecutionState.Completed);
        execution.ToolCalls.ShouldBeEmpty();
    }

    [Fact]
    public void A_running_execution_has_no_audit_record_yet() =>
        Should.Throw<InvalidOperationException>(() => Started().ToRecord("{}"));

    [Theory]
    [InlineData(ExecutionState.Running, ExecutionState.Completed, true)]
    [InlineData(ExecutionState.Running, ExecutionState.Abstained, true)]
    [InlineData(ExecutionState.Running, ExecutionState.Failed, true)]
    [InlineData(ExecutionState.Completed, ExecutionState.Failed, false)]
    [InlineData(ExecutionState.Abstained, ExecutionState.Completed, false)]
    [InlineData(ExecutionState.Failed, ExecutionState.Failed, false)]
    public void An_execution_moves_only_as_the_table_allows(ExecutionState from, ExecutionState to, bool allowed) =>
        ExecutionStateTransitions.CanMove(from, to).ShouldBe(allowed);
}
