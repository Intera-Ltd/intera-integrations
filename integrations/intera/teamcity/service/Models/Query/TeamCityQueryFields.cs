using Intera.Common.Infrastructure.Query;

namespace Intera.TeamCity.Api.Models.Query;

public static class BuildTypeQueryFields
{
    public static readonly IReadOnlyDictionary<string, InMemoryQueryField> All = InMemoryQueryFieldsBuilder.Build(
        "project_id",
        "project_name",
        "build_type_id",
        "build_type_name",
        "build_type_web_url",
        "paused",
        "last_build_id",
        "last_build_number",
        "last_build_status",
        "last_build_success",
        "last_build_status_text",
        "last_build_branch",
        "last_build_start_date",
        "last_build_finish_date",
        "last_build_web_url",
        "last_build_tests_total",
        "last_build_tests_passed",
        "last_build_tests_failed",
        "last_build_tests_ignored",
        "last_build_tests_muted",
        "last_build_tests_new_failed",
        "running_builds",
        "queued_builds");
}

public static class TestResultQueryFields
{
    public static readonly IReadOnlyDictionary<string, InMemoryQueryField> All = InMemoryQueryFieldsBuilder.Build(
        "project_id",
        "project_name",
        "build_type_id",
        "build_type_name",
        "build_type_web_url",
        "paused",
        "build_id",
        "build_number",
        "build_status",
        "build_status_text",
        "build_branch",
        "build_start_date",
        "build_finish_date",
        "build_web_url",
        "from_latest_build",
        "test_id",
        "test_name",
        "test_package",
        "test_suite",
        "test_class",
        "test_method",
        "status",
        "passed",
        "ignored",
        "muted",
        "currently_muted",
        "currently_investigated",
        "new_failure",
        "duration_ms",
        "details",
        "ignore_details");
}
