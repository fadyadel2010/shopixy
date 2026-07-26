import { DndContext, PointerSensor, closestCenter, useSensor, useSensors } from '@dnd-kit/core'
import { SortableContext, arrayMove, rectSortingStrategy, useSortable } from '@dnd-kit/sortable'
import { CSS } from '@dnd-kit/utilities'
import MediaCard from './MediaCard'

function SortableMediaCard({
    item,
    isPrimary,
    disabled,
    onDelete,
    onSetPrimary,
    onPreview
}) {
    const {
        attributes,
        listeners,
        setNodeRef,
        transform,
        transition,
        isDragging
    } = useSortable({ id: item.id, disabled })

    const style = {
        transform: CSS.Transform.toString(transform),
        transition
    }

    return (
        <div ref={setNodeRef} style={style}>
            <MediaCard
                item={item}
                isPrimary={isPrimary}
                isDragging={isDragging}
                disabled={disabled}
                dragHandleProps={{ ...attributes, ...listeners }}
                onDelete={onDelete}
                onSetPrimary={onSetPrimary}
                onPreview={onPreview}
            />
        </div>
    )
}

export default function MediaGalleryGrid({
    items,
    primaryMediaId,
    onReorder,
    onDelete,
    onSetPrimary,
    onPreview,
    disabled = false
}) {
    const sensors = useSensors(
        useSensor(PointerSensor, {
            activationConstraint: {
                distance: 8
            }
        })
    )

    const handleDragEnd = ({ active, over }) => {
        if (disabled || !over || active.id === over.id) {
            return
        }

        const oldIndex = items.findIndex(item => item.id === active.id)
        const newIndex = items.findIndex(item => item.id === over.id)

        if (oldIndex < 0 || newIndex < 0 || oldIndex === newIndex) {
            return
        }

        const reorderedItems = arrayMove(items, oldIndex, newIndex).map((item, index) => ({
            ...item,
            position: index + 1
        }))

        onReorder(reorderedItems)
    }

    if (!items.length) {
        return null
    }

    return (
        <DndContext sensors={sensors} collisionDetection={closestCenter} onDragEnd={handleDragEnd}>
            <SortableContext items={items.map(item => item.id)} strategy={rectSortingStrategy}>
                <div className="grid grid-cols-1 gap-4 md:grid-cols-2 xl:grid-cols-3">
                    {items.map((item) => (
                        <SortableMediaCard
                            key={item.id}
                            item={item}
                            isPrimary={item.id === primaryMediaId}
                            disabled={disabled}
                            onDelete={onDelete}
                            onSetPrimary={onSetPrimary}
                            onPreview={onPreview}
                        />
                    ))}
                </div>
            </SortableContext>
        </DndContext>
    )
}
