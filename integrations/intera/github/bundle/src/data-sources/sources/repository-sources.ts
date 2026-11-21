import type { GitHubObject } from '../../github/github-client';
import {
  LOOKUP,
  bool,
  date,
  flag,
  joined,
  number,
  num,
  obj,
  str,
  text,
  type IGitHubSourceDefinition,
} from '../source-definition';

export const repositoriesSource: IGitHubSourceDefinition = {
  id: 'github_repositories',
  name: 'Repositories',
  description:
    "The owner's repositories in the instance's scope, archived ones included (`archived`). `pushed_at` is the last " +
    'push to any branch — the column a stale-repository check filters on. `open_issues` counts open pull requests ' +
    'too (GitHub counts them together). Needs Metadata: read.',
  fields: [
    text('repo_id', 'Repository ID', LOOKUP.repository),
    text('name', 'Repository'),
    text('full_name', 'Full name'),
    text('owner', 'Owner'),
    text('visibility', 'Visibility'),
    flag('private', 'Private'),
    flag('archived', 'Archived'),
    flag('disabled', 'Disabled'),
    flag('fork', 'Fork'),
    flag('is_template', 'Template'),
    text('default_branch', 'Default branch'),
    text('description', 'Description'),
    text('language', 'Language'),
    text('topics', 'Topics'),
    number('stars', 'Stars'),
    number('forks', 'Forks'),
    number('open_issues', 'Open issues and pull requests'),
    number('size_kb', 'Size, KB'),
    date('created_at', 'Created'),
    date('updated_at', 'Updated'),
    date('pushed_at', 'Last push'),
    text('html_url', 'Link'),
  ],
  async load(reader) {
    const repositories = await reader.scopedRepositories();
    return repositories.map((repository: GitHubObject) => ({
      repo_id: str(repository.id),
      name: str(repository.name),
      full_name: str(repository.full_name),
      owner: str(obj(repository.owner)?.login),
      visibility: str(repository.visibility) ?? (repository.private === true ? 'private' : 'public'),
      private: bool(repository.private),
      archived: bool(repository.archived),
      disabled: bool(repository.disabled),
      fork: bool(repository.fork),
      is_template: bool(repository.is_template),
      default_branch: str(repository.default_branch),
      description: str(repository.description),
      language: str(repository.language),
      topics: joined(repository.topics, str),
      stars: num(repository.stargazers_count),
      forks: num(repository.forks_count),
      open_issues: num(repository.open_issues_count),
      size_kb: num(repository.size),
      created_at: str(repository.created_at),
      updated_at: str(repository.updated_at),
      pushed_at: str(repository.pushed_at),
      html_url: str(repository.html_url),
    }));
  },
};
