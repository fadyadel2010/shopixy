export const ProductDetailsSkeleton = () => {
  return (
    <div className="grid gap-8 lg:grid-cols-[minmax(0,1.05fr)_minmax(20rem,0.95fr)]">
      <div className="space-y-4">
        <div className="skeleton-wave aspect-square rounded-[2rem]" />
        <div className="grid grid-cols-4 gap-3 sm:grid-cols-5">
          {Array.from({ length: 5 }).map((_, index) => (
            <div key={index} className="skeleton-wave aspect-square rounded-2xl" />
          ))}
        </div>
      </div>

      <div className="space-y-5">
        <div className="space-y-3">
          <div className="skeleton-wave h-4 w-24 rounded-full" />
          <div className="skeleton-wave h-10 w-4/5 rounded-full" />
          <div className="skeleton-wave h-5 w-40 rounded-full" />
        </div>

        <div className="skeleton-wave h-24 rounded-3xl" />
        <div className="skeleton-wave h-32 rounded-3xl" />
        <div className="skeleton-wave h-40 rounded-3xl" />
      </div>
    </div>
  );
};
