# Contributing to CopyTrade Platform

Thank you for your interest in contributing! We welcome contributions from the community.

## Getting Started

1. Fork the repository
2. Clone your fork: `git clone https://github.com/YOUR_USERNAME/copytrade-platform.git`
3. Create a branch: `git checkout -b feature/your-feature-name`
4. Make your changes
5. Test your changes thoroughly
6. Commit with clear messages
7. Push to your fork
8. Submit a Pull Request

## Development Setup

```bash
# Install .NET 8.0 SDK
# Install Docker and Docker Compose

# Clone the repo
git clone <repo-url>
cd CopyTradePlatform

# Copy environment template
cp .env.template .env

# Start services
docker-compose up -d

# Check logs
docker-compose logs -f api
```

## Coding Standards

- Follow C# coding conventions
- Use meaningful variable and method names
- Add XML documentation comments for public APIs
- Write unit tests for new features
- Keep methods focused and concise
- Use async/await for I/O operations

## Commit Message Guidelines

Format: `<type>: <description>`

Types:
- `feat`: New feature
- `fix`: Bug fix
- `docs`: Documentation changes
- `style`: Code style changes (formatting)
- `refactor`: Code refactoring
- `test`: Adding tests
- `chore`: Maintenance tasks

Examples:
- `feat: Add support for Interactive Brokers`
- `fix: Resolve race condition in signal processing`
- `docs: Update API documentation`

## Pull Request Process

1. Update README.md with details of changes if needed
2. Update documentation for any API changes
3. Add tests for new functionality
4. Ensure all tests pass
5. Update CHANGELOG.md
6. Request review from maintainers

## Testing

```bash
# Run tests
dotnet test

# Run with coverage
dotnet test /p:CollectCoverage=true

# Docker tests
docker-compose -f docker-compose.test.yml up
```

## Code Review

All submissions require review. We use GitHub pull requests for this purpose.

## Areas for Contribution

### High Priority
- [ ] Web dashboard UI
- [ ] Email notification system
- [ ] Advanced analytics
- [ ] Performance optimizations
- [ ] Security hardening
- [ ] Comprehensive testing

### Medium Priority
- [ ] Mobile app
- [ ] Additional broker integrations
- [ ] Machine learning signal filtering
- [ ] Social trading features
- [ ] Multi-language support

### Good First Issues
- [ ] Documentation improvements
- [ ] Code comments
- [ ] Example configurations
- [ ] Bug fixes
- [ ] UI improvements

## Security

If you discover a security vulnerability, please email security@copytrade.com instead of using the issue tracker.

## License

By contributing, you agree that your contributions will be licensed under the MIT License.

## Questions?

Feel free to open an issue for:
- Bug reports
- Feature requests
- Questions about the codebase
- Suggestions for improvements

## Community

- GitHub Issues: For bug reports and feature requests
- GitHub Discussions: For questions and discussions
- Discord: [Join our Discord](#) (if available)

Thank you for contributing! 🚀
