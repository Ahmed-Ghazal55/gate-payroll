if (process.env.NEXT_PUBLIC_MOCK_API === '1') {
  console.error('❌ Error: Production build cannot have NEXT_PUBLIC_MOCK_API=1. Mocks are for local synthetic tests only.');
  process.exit(1);
}
console.log('✓ Build check: MOCK_API is disabled for production.');
