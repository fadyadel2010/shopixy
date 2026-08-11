import { StrictMode, type ReactNode } from 'react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { cleanup, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'

import i18n from '../../../i18n/config'
import { ToastProvider } from '../../../components/Toast'
import { CheckoutFieldsPage } from './pages/CheckoutFieldsPage'
import checkoutFieldsService from './services/checkoutFieldsService'

vi.mock('@dnd-kit/core', () => ({
    DndContext: ({ children }: { children: ReactNode }) => <>{children}</>,
    PointerSensor: vi.fn(),
    closestCenter: vi.fn(),
    useSensor: vi.fn(() => ({})),
    useSensors: vi.fn(() => []),
}))

vi.mock('@dnd-kit/sortable', () => ({
    SortableContext: ({ children }: { children: ReactNode }) => <>{children}</>,
    verticalListSortingStrategy: {},
    arrayMove: (items: unknown[], oldIndex: number, newIndex: number) => {
        const next = [...items]
        const [item] = next.splice(oldIndex, 1)
        next.splice(newIndex, 0, item)
        return next
    },
    useSortable: () => ({
        attributes: {},
        listeners: {},
        setNodeRef: vi.fn(),
        transform: null,
        transition: undefined,
        isDragging: false,
    }),
}))

vi.mock('@dnd-kit/utilities', () => ({ CSS: { Transform: { toString: () => undefined } } }))

vi.mock('./services/checkoutFieldsService', () => ({
    default: {
        getFields: vi.fn(),
        initializeSystemFields: vi.fn(),
        createField: vi.fn(),
        updateField: vi.fn(),
        deleteField: vi.fn(),
        reorderFields: vi.fn(),
    },
}))

const systemField = {
    id: '11111111-1111-4111-8111-111111111111',
    fieldKind: 'SYSTEM',
    fieldType: 'TEXT',
    systemFieldKey: 'full_name',
    label: 'Full Name',
    helpText: 'Customer legal name',
    isEnabled: true,
    isRequired: true,
    displayOrder: 10,
    configuration: { schemaVersion: 1, minLength: 2 },
}

const customField = {
    id: '22222222-2222-4222-8222-222222222222',
    fieldKind: 'CUSTOM',
    fieldType: 'SELECT',
    systemFieldKey: null,
    label: 'T-shirt Size',
    helpText: 'Choose your preferred size',
    isEnabled: false,
    isRequired: false,
    displayOrder: 20,
    configuration: {
        schemaVersion: 1,
        options: [
            { value: 'Small', label: 'Small' },
            { value: 'Large', label: 'Large' },
        ],
    },
}

const renderPage = () => render(
    <ToastProvider>
        <CheckoutFieldsPage />
    </ToastProvider>,
)

const editorLabelInput = () => document.querySelector('#checkout-field-label') as HTMLInputElement
const editorHelpText = () => document.querySelector('#checkout-field-help-text') as HTMLTextAreaElement
const editorFieldType = () => document.querySelector('#checkout-field-type') as HTMLSelectElement
const uploadFormat = (name: string) => screen.getByRole('checkbox', { name }) as HTMLInputElement

describe('Checkout Fields admin UI', () => {
    beforeEach(async () => {
        await i18n.changeLanguage('en')
        vi.mocked(checkoutFieldsService.getFields).mockResolvedValue({ fields: [systemField, customField] })
        vi.mocked(checkoutFieldsService.initializeSystemFields).mockResolvedValue({ fields: [systemField] })
        vi.mocked(checkoutFieldsService.createField).mockResolvedValue(customField)
        vi.mocked(checkoutFieldsService.updateField).mockResolvedValue(customField)
        vi.mocked(checkoutFieldsService.deleteField).mockResolvedValue(undefined)
        vi.mocked(checkoutFieldsService.reorderFields).mockResolvedValue({ fields: [customField, systemField] })
    })

    afterEach(() => {
        cleanup()
        vi.clearAllMocks()
    })

    it('renders the screen and loads fields from the API', async () => {
        renderPage()

        expect(await screen.findByRole('heading', { name: 'Checkout Fields' })).toBeInTheDocument()
        expect(checkoutFieldsService.getFields).toHaveBeenCalledTimes(1)
        expect(screen.getByText('Control the customer information collected during storefront checkout.')).toBeInTheDocument()
        expect(screen.getByText('Full Name')).toBeInTheDocument()
        expect(screen.getByText('T-shirt Size')).toBeInTheDocument()
    })

    it('finishes loading when mounted under React Strict Mode', async () => {
        render(
            <StrictMode>
                <ToastProvider>
                    <CheckoutFieldsPage />
                </ToastProvider>
            </StrictMode>,
        )

        expect(await screen.findByText('Full Name')).toBeInTheDocument()
        expect(screen.queryByText('Loading checkout fields...')).not.toBeInTheDocument()
    })

    it('renders system/custom distinction, required state, and enabled state', async () => {
        renderPage()

        const fullNameRow = (await screen.findByText('Full Name')).closest('li')!
        const sizeRow = screen.getByText('T-shirt Size').closest('li')!
        expect(within(fullNameRow).getByText('System')).toBeInTheDocument()
        expect(within(fullNameRow).getByText('Required')).toBeInTheDocument()
        expect(within(fullNameRow).getByText('Shown in Checkout')).toBeInTheDocument()
        expect(within(sizeRow).getByText('Custom')).toBeInTheDocument()
        expect(within(sizeRow).getByText('Optional')).toBeInTheDocument()
        expect(within(sizeRow).getByText('Hidden from Checkout')).toBeInTheDocument()
    })

    it('creates a custom field through the API', async () => {
        const user = userEvent.setup()
        renderPage()

        await user.click(await screen.findByRole('button', { name: 'Add Custom Field' }))
        await waitFor(() => expect(editorLabelInput()).toHaveFocus())
        expect(editorHelpText().tagName).toBe('TEXTAREA')
        await user.type(editorLabelInput(), 'Delivery Window')
        expect(editorLabelInput()).toHaveValue('Delivery Window')
        await user.type(editorHelpText(), 'Preferred delivery time')
        expect(editorHelpText()).toHaveValue('Preferred delivery time')
        await user.selectOptions(editorFieldType(), 'DATE')
        await user.click(screen.getByRole('button', { name: 'Save' }))

        await waitFor(() => expect(checkoutFieldsService.createField).toHaveBeenCalledWith(expect.objectContaining({
            fieldType: 'DATE',
            label: 'Delivery Window',
            helpText: 'Preferred delivery time',
            isEnabled: true,
            isRequired: false,
            configuration: { schemaVersion: 1 },
        })))
    })

    it('edits a custom field and sends supported field configuration', async () => {
        const user = userEvent.setup()
        renderPage()

        const sizeRow = (await screen.findByText('T-shirt Size')).closest('li')!
        await user.click(within(sizeRow).getByRole('button', { name: 'Edit' }))
        await waitFor(() => expect(editorLabelInput()).toHaveFocus())
        await user.clear(editorLabelInput())
        expect(editorLabelInput()).toHaveValue('')
        await user.type(editorLabelInput(), 'Order Size')
        expect(editorLabelInput()).toHaveValue('Order Size')
        await user.click(screen.getByLabelText(/Show in Checkout/))
        await user.click(screen.getByLabelText('Require at Checkout'))
        await user.click(screen.getByRole('button', { name: 'Save' }))

        await waitFor(() => expect(checkoutFieldsService.updateField).toHaveBeenCalledWith(customField.id, expect.objectContaining({
            fieldType: 'SELECT',
            label: 'Order Size',
            isEnabled: true,
            isRequired: true,
            configuration: expect.objectContaining({
                schemaVersion: 1,
                options: [
                    { value: 'Small', label: 'Small' },
                    { value: 'Large', label: 'Large' },
                ],
            }),
        })))
    })

    it('sends an empty string when existing help text is cleared', async () => {
        const user = userEvent.setup()
        renderPage()

        const sizeRow = (await screen.findByText('T-shirt Size')).closest('li')!
        await user.click(within(sizeRow).getByRole('button', { name: 'Edit' }))
        await waitFor(() => expect(editorHelpText()).toHaveValue('Choose your preferred size'))
        await user.clear(editorHelpText())
        await user.click(screen.getByRole('button', { name: 'Save' }))

        await waitFor(() => expect(checkoutFieldsService.updateField).toHaveBeenCalledWith(
            customField.id,
            expect.objectContaining({ helpText: '' }),
        ))
    })

    it('validates required label before submission', async () => {
        const user = userEvent.setup()
        renderPage()

        await user.click(await screen.findByRole('button', { name: 'Add Custom Field' }))
        await waitFor(() => expect(editorLabelInput()).toHaveFocus())
        await user.click(screen.getByRole('button', { name: 'Save' }))

        expect(await screen.findByText('Label is required.')).toBeInTheDocument()
        expect(checkoutFieldsService.createField).not.toHaveBeenCalled()
    })

    it('edits SELECT/RADIO options and rejects empty or duplicate values before submission', async () => {
        const user = userEvent.setup()
        renderPage()

        await user.click(await screen.findByRole('button', { name: 'Add Custom Field' }))
        await waitFor(() => expect(editorLabelInput()).toHaveFocus())
        await user.type(editorLabelInput(), 'Gift Choice')
        await user.selectOptions(editorFieldType(), 'RADIO')
        await user.type(screen.getByPlaceholderText('Option value'), 'Wrap')
        await user.click(screen.getByRole('button', { name: 'Add Option' }))
        const optionInputs = screen.getAllByPlaceholderText('Option value')
        await user.type(optionInputs[1], 'Wrap')
        await user.click(screen.getByRole('button', { name: 'Save' }))

        expect(await screen.findByText('Option values must be unique.')).toBeInTheDocument()
        expect(checkoutFieldsService.createField).not.toHaveBeenCalled()
    })

    it('reorders fields through the order endpoint', async () => {
        const user = userEvent.setup()
        renderPage()

        await user.click(await screen.findByRole('button', { name: 'Move Full Name down' }))

        await waitFor(() => expect(checkoutFieldsService.reorderFields).toHaveBeenCalledWith([
            { id: customField.id, displayOrder: 10 },
            { id: systemField.id, displayOrder: 20 },
        ]))
    })

    it('does not expose archive for system fields and archives custom fields through DELETE', async () => {
        const user = userEvent.setup()
        renderPage()

        const fullNameRow = (await screen.findByText('Full Name')).closest('li')!
        const sizeRow = screen.getByText('T-shirt Size').closest('li')!
        expect(within(fullNameRow).queryByRole('button', { name: 'Archive' })).not.toBeInTheDocument()
        await user.click(within(sizeRow).getByRole('button', { name: 'Archive' }))
        await user.click(within(screen.getByRole('dialog')).getByRole('button', { name: 'Archive' }))

        await waitFor(() => expect(checkoutFieldsService.deleteField).toHaveBeenCalledWith(customField.id))
    })

    it('explains locked system field types and hidden checkout semantics', async () => {
        const user = userEvent.setup()
        renderPage()

        const fullNameRow = (await screen.findByText('Full Name')).closest('li')!
        await user.click(within(fullNameRow).getByRole('button', { name: 'Edit' }))

        expect(editorFieldType()).toBeDisabled()
        expect(screen.getByText('System field type is locked because it maps to checkout address, customer, and order data.')).toBeInTheDocument()
        expect(screen.getByText('When off, this field is not shown or required in storefront checkout.')).toBeInTheDocument()
    })

    it('initializes system fields only when they are absent', async () => {
        vi.mocked(checkoutFieldsService.getFields).mockResolvedValueOnce({ fields: [] })
        vi.mocked(checkoutFieldsService.initializeSystemFields).mockResolvedValueOnce({ fields: [systemField] })

        renderPage()

        expect(await screen.findByText('Full Name')).toBeInTheDocument()
        expect(checkoutFieldsService.initializeSystemFields).toHaveBeenCalledTimes(1)
    })

    it('does not initialize when system fields are present and treats empty canonical response as empty state', async () => {
        vi.mocked(checkoutFieldsService.getFields).mockResolvedValueOnce({ fields: [systemField] })
        const firstRender = renderPage()
        expect(await screen.findByText('Full Name')).toBeInTheDocument()
        expect(checkoutFieldsService.initializeSystemFields).not.toHaveBeenCalled()
        firstRender.unmount()

        vi.clearAllMocks()
        vi.mocked(checkoutFieldsService.getFields).mockResolvedValueOnce({ fields: [] })
        vi.mocked(checkoutFieldsService.initializeSystemFields).mockResolvedValueOnce({ fields: [] })
        renderPage()
        expect(await screen.findByText('No checkout fields configured')).toBeInTheDocument()
        expect(screen.queryByText('Unable to load checkout fields')).not.toBeInTheDocument()
    })

    it('renders API errors and permission errors gracefully', async () => {
        vi.mocked(checkoutFieldsService.getFields).mockRejectedValueOnce({ response: { status: 500, data: { message: 'Server failed' } } })
        const firstRender = renderPage()
        expect(await screen.findByText('Unable to load checkout fields')).toBeInTheDocument()
        firstRender.unmount()

        vi.clearAllMocks()
        vi.mocked(checkoutFieldsService.getFields).mockRejectedValueOnce({ response: { status: 403, data: { message: 'Forbidden' } } })
        renderPage()
        expect(await screen.findByText('Permission required')).toBeInTheDocument()
        expect(screen.getByText('Only POS owners and admins can manage checkout fields.')).toBeInTheDocument()
    })

    it('has English and Arabic labels for the screen', async () => {
        expect(i18n.exists('checkoutFields.title', { lng: 'en' })).toBe(true)
        expect(i18n.exists('checkoutFields.title', { lng: 'ar' })).toBe(true)
        expect(i18n.exists('checkoutFields.types.SELECT', { lng: 'en' })).toBe(true)
        expect(i18n.exists('checkoutFields.types.SELECT', { lng: 'ar' })).toBe(true)
    })

    it('defaults image upload fields to safe image formats and persists MIME values', async () => {
        const user = userEvent.setup()
        renderPage()

        await user.click(await screen.findByRole('button', { name: 'Add Custom Field' }))
        await waitFor(() => expect(editorLabelInput()).toHaveFocus())
        await user.type(editorLabelInput(), 'Attachment')
        await user.selectOptions(editorFieldType(), 'IMAGE')

        expect(screen.getByRole('group', { name: 'Allowed image formats' })).toBeInTheDocument()
        expect(uploadFormat('JPG / JPEG')).toBeChecked()
        expect(uploadFormat('PNG')).toBeChecked()
        expect(uploadFormat('WEBP')).toBeChecked()
        expect(screen.queryByLabelText('Allowed MIME Types')).not.toBeInTheDocument()

        await user.type(screen.getByLabelText('Maximum File Size (MB)'), '4')
        await user.click(screen.getByRole('button', { name: 'Save' }))

        await waitFor(() => expect(checkoutFieldsService.createField).toHaveBeenCalledWith(expect.objectContaining({
            fieldType: 'IMAGE',
            configuration: expect.objectContaining({
                allowedMimeTypes: ['image/jpeg', 'image/png', 'image/webp'],
                maxFileSize: 4 * 1024 * 1024,
                maxFiles: 1,
            }),
        })))
    })

    it('persists image format deselection and rejects empty image selections', async () => {
        const user = userEvent.setup()
        renderPage()

        await user.click(await screen.findByRole('button', { name: 'Add Custom Field' }))
        await waitFor(() => expect(editorLabelInput()).toHaveFocus())
        await user.type(editorLabelInput(), 'Logo')
        await user.selectOptions(editorFieldType(), 'IMAGE')
        await user.click(uploadFormat('WEBP'))
        await user.click(screen.getByRole('button', { name: 'Save' }))

        await waitFor(() => expect(checkoutFieldsService.createField).toHaveBeenCalledWith(expect.objectContaining({
            fieldType: 'IMAGE',
            configuration: expect.objectContaining({
                allowedMimeTypes: ['image/jpeg', 'image/png'],
            }),
        })))

        vi.mocked(checkoutFieldsService.createField).mockClear()
        await user.click(await screen.findByRole('button', { name: 'Add Custom Field' }))
        await waitFor(() => expect(editorLabelInput()).toHaveFocus())
        await user.type(editorLabelInput(), 'Empty upload')
        await user.selectOptions(editorFieldType(), 'IMAGE')
        await user.click(uploadFormat('JPG / JPEG'))
        await user.click(uploadFormat('PNG'))
        await user.click(uploadFormat('WEBP'))
        await user.click(screen.getByRole('button', { name: 'Save' }))

        expect(await screen.findByText('Select at least one supported image format.')).toBeInTheDocument()
        expect(checkoutFieldsService.createField).not.toHaveBeenCalled()
    })

    it('maps existing image MIME configuration back to selected image formats', async () => {
        const user = userEvent.setup()
        const imageField = {
            ...customField,
            id: '44444444-4444-4444-8444-444444444444',
            fieldType: 'IMAGE',
            label: 'Artwork',
            configuration: {
                schemaVersion: 1,
                allowedMimeTypes: ['image/jpeg', 'image/png'],
                maxFileSize: 5 * 1024 * 1024,
                maxFiles: 1,
            },
        }
        vi.mocked(checkoutFieldsService.getFields).mockResolvedValueOnce({ fields: [systemField, imageField] })
        renderPage()

        const row = (await screen.findByText('Artwork')).closest('li')!
        await user.click(within(row).getByRole('button', { name: 'Edit' }))

        expect(uploadFormat('JPG / JPEG')).toBeChecked()
        expect(uploadFormat('PNG')).toBeChecked()
        expect(uploadFormat('WEBP')).not.toBeChecked()
    })

    it('preserves multiple image max files, max size, and selected formats', async () => {
        const user = userEvent.setup()
        const multiImageField = {
            ...customField,
            id: '55555555-5555-4555-8555-555555555555',
            fieldType: 'MULTI_IMAGE',
            label: 'Gallery',
            configuration: {
                schemaVersion: 1,
                allowedMimeTypes: ['image/jpeg', 'image/png', 'image/webp'],
                maxFileSize: 6 * 1024 * 1024,
                maxFiles: 8,
            },
        }
        vi.mocked(checkoutFieldsService.getFields).mockResolvedValueOnce({ fields: [systemField, multiImageField] })
        renderPage()

        const row = (await screen.findByText('Gallery')).closest('li')!
        await user.click(within(row).getByRole('button', { name: 'Edit' }))
        expect(screen.getByLabelText('Maximum Files')).toHaveValue(8)
        expect(screen.getByLabelText('Maximum File Size (MB)')).toHaveValue(6)
        await user.click(uploadFormat('PNG'))
        await user.click(screen.getByRole('button', { name: 'Save' }))

        await waitFor(() => expect(checkoutFieldsService.updateField).toHaveBeenCalledWith(
            multiImageField.id,
            expect.objectContaining({
                configuration: expect.objectContaining({
                    allowedMimeTypes: ['image/jpeg', 'image/webp'],
                    maxFileSize: 6 * 1024 * 1024,
                    maxFiles: 8,
                }),
            }),
        ))
    })

    it('renders file type presets and maps selected file formats to canonical MIME values', async () => {
        const user = userEvent.setup()
        renderPage()

        await user.click(await screen.findByRole('button', { name: 'Add Custom Field' }))
        await waitFor(() => expect(editorLabelInput()).toHaveFocus())
        await user.type(editorLabelInput(), 'Documents')
        await user.selectOptions(editorFieldType(), 'FILE')

        expect(screen.getByRole('group', { name: 'Allowed file types' })).toBeInTheDocument()
        expect(uploadFormat('PDF')).toBeChecked()
        expect(uploadFormat('Word')).toBeChecked()
        expect(screen.queryByLabelText('Allowed MIME Types')).not.toBeInTheDocument()

        await user.click(uploadFormat('JPG / JPEG'))
        await user.click(uploadFormat('PNG'))
        await user.click(uploadFormat('WEBP'))
        await user.type(screen.getByLabelText('Maximum File Size (MB)'), '10')
        await user.click(screen.getByRole('button', { name: 'Save' }))

        await waitFor(() => expect(checkoutFieldsService.createField).toHaveBeenCalledWith(expect.objectContaining({
            fieldType: 'FILE',
            configuration: expect.objectContaining({
                allowedMimeTypes: ['application/pdf', 'application/msword'],
                maxFileSize: 10 * 1024 * 1024,
                maxFiles: 1,
            }),
        })))
    })

    it('rejects empty file type selections and does not provide arbitrary MIME entry', async () => {
        const user = userEvent.setup()
        renderPage()

        await user.click(await screen.findByRole('button', { name: 'Add Custom Field' }))
        await waitFor(() => expect(editorLabelInput()).toHaveFocus())
        await user.type(editorLabelInput(), 'Documents')
        await user.selectOptions(editorFieldType(), 'FILE')

        for (const label of ['JPG / JPEG', 'PNG', 'WEBP', 'PDF', 'Word']) {
            await user.click(uploadFormat(label))
        }
        await user.click(screen.getByRole('button', { name: 'Save' }))

        expect(await screen.findByText('Select at least one supported file type.')).toBeInTheDocument()
        expect(screen.queryByLabelText('Allowed MIME Types')).not.toBeInTheDocument()
        expect(checkoutFieldsService.createField).not.toHaveBeenCalled()
    })

    it('keeps legacy configured MIME values visible and preserves them in the payload', async () => {
        const user = userEvent.setup()
        const legacyField = {
            ...customField,
            id: '66666666-6666-4666-8666-666666666666',
            fieldType: 'IMAGE',
            label: 'Legacy upload',
            configuration: {
                schemaVersion: 1,
                allowedMimeTypes: ['image/jpeg', 'image/gif'],
                maxFileSize: 4 * 1024 * 1024,
                maxFiles: 1,
            },
        }
        vi.mocked(checkoutFieldsService.getFields).mockResolvedValueOnce({ fields: [systemField, legacyField] })
        renderPage()

        const row = (await screen.findByText('Legacy upload')).closest('li')!
        await user.click(within(row).getByRole('button', { name: 'Edit' }))

        expect(uploadFormat('JPG / JPEG')).toBeChecked()
        expect(screen.getByText('Legacy configured formats')).toBeInTheDocument()
        expect(screen.getByText('image/gif')).toBeInTheDocument()
        await user.clear(editorHelpText())
        await user.type(editorHelpText(), 'Preserve old formats')
        await user.click(screen.getByRole('button', { name: 'Save' }))

        await waitFor(() => expect(checkoutFieldsService.updateField).toHaveBeenCalledWith(
            legacyField.id,
            expect.objectContaining({
                helpText: 'Preserve old formats',
                configuration: expect.objectContaining({
                    allowedMimeTypes: ['image/jpeg', 'image/gif'],
                    maxFileSize: 4 * 1024 * 1024,
                    maxFiles: 1,
                }),
            }),
        ))
    })

    it('round trips upload configuration without losing conditional rules or limits', async () => {
        const user = userEvent.setup()
        const sourceField = {
            ...customField,
            id: '77777777-7777-4777-8777-777777777777',
            label: 'Need upload?',
        }
        const uploadField = {
            ...customField,
            id: '88888888-8888-4888-8888-888888888888',
            fieldType: 'FILE',
            label: 'Certificate',
            configuration: {
                schemaVersion: 1,
                allowedMimeTypes: ['application/pdf'],
                maxFileSize: 12 * 1024 * 1024,
                maxFiles: 1,
                conditionalRules: [
                    {
                        ruleId: 'rule1',
                        conditionMode: 'ALL',
                        conditions: [{ sourceFieldId: sourceField.id, operator: 'EQUALS', comparisonValue: 'Small' }],
                        actions: ['SHOW'],
                    },
                ],
            },
        }
        vi.mocked(checkoutFieldsService.getFields).mockResolvedValueOnce({ fields: [systemField, sourceField, uploadField] })
        renderPage()

        const row = (await screen.findByText('Certificate')).closest('li')!
        await user.click(within(row).getByRole('button', { name: 'Edit' }))
        await user.click(uploadFormat('Word'))
        await user.click(screen.getByRole('button', { name: 'Save' }))

        await waitFor(() => expect(checkoutFieldsService.updateField).toHaveBeenCalledWith(
            uploadField.id,
            expect.objectContaining({
                configuration: expect.objectContaining({
                    allowedMimeTypes: ['application/pdf', 'application/msword'],
                    maxFileSize: 12 * 1024 * 1024,
                    maxFiles: 1,
                    conditionalRules: uploadField.configuration.conditionalRules,
                }),
            }),
        ))
    })

    it('preserves upload configuration when editing an unrelated property', async () => {
        const user = userEvent.setup()
        const uploadField = {
            ...customField,
            id: '33333333-3333-4333-8333-333333333333',
            fieldType: 'FILE',
            label: 'Documents',
            configuration: {
                schemaVersion: 1,
                allowedMimeTypes: ['application/pdf'],
                maxFileSize: 8 * 1024 * 1024,
                maxFiles: 1,
            },
        }
        vi.mocked(checkoutFieldsService.getFields).mockResolvedValueOnce({ fields: [systemField, uploadField] })
        renderPage()

        const row = (await screen.findByText('Documents')).closest('li')!
        await user.click(within(row).getByRole('button', { name: 'Edit' }))
        await user.clear(editorHelpText())
        await user.type(editorHelpText(), 'Updated help')
        await user.click(screen.getByRole('button', { name: 'Save' }))

        await waitFor(() => expect(checkoutFieldsService.updateField).toHaveBeenCalledWith(
            uploadField.id,
            expect.objectContaining({
                configuration: expect.objectContaining({
                    allowedMimeTypes: ['application/pdf'],
                    maxFileSize: 8 * 1024 * 1024,
                    maxFiles: 1,
                }),
            }),
        ))
    })
})
