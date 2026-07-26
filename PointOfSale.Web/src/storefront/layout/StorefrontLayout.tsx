import React from 'react'

export const StorefrontLayout: React.FC<{ children: React.ReactNode }> = ({ children }) => (
  <div className="flex min-h-full flex-col bg-background text-text">
    {children}
  </div>
)

export default StorefrontLayout
