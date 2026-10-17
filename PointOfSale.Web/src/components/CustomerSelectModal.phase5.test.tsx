import { act, fireEvent, render, screen } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import CustomerSelectModal from './CustomerSelectModal'

const fixtures = vi.hoisted(() => ({post:vi.fn(),showError:vi.fn()}))
vi.mock('../services/api', () => ({default:{post:fixtures.post}}))
vi.mock('../contexts/SettingsContext', () => ({useSettings:()=>({formatCurrency:String})}))
vi.mock('./Toast', () => ({useToast:()=>({showError:fixtures.showError})}))
vi.mock('../hooks/useAsyncLookup', () => ({useAsyncLookup:()=>({query:'',setQuery:vi.fn(),options:[],loading:false,error:null,hasMore:false,loadMore:vi.fn()})}))
vi.mock('react-i18next', () => ({useTranslation:()=>({t:(key:string)=>key})}))

afterEach(() => vi.restoreAllMocks())
describe('customer creation failure privacy and recovery', () => {
  it('retains the form, reports failure and logs only HTTP status', async () => {
    const log = vi.spyOn(console,'error').mockImplementation(()=>{})
    fixtures.post.mockRejectedValue({message:'sensitive fixture',response:{status:503,data:{phone:'sensitive fixture'}},config:{headers:{Authorization:'sensitive fixture'}}})
    const {container}=render(<CustomerSelectModal onSelect={vi.fn()} onClose={vi.fn()} />)
    fireEvent.click(screen.getByRole('button',{name:'customerSelectModal.createNewCustomer'}))
    const form=container.querySelector('form')!
    fireEvent.change(form.querySelector('input[type=text]')!,{target:{value:'Synthetic'}})
    fireEvent.change(form.querySelector('input[type=tel]')!,{target:{value:'synthetic'}})
    await act(async()=>fireEvent.submit(form))
    expect(fixtures.showError).toHaveBeenCalledWith('customerSelectModal.failedToCreate')
    expect(log).toHaveBeenCalledExactlyOnceWith('Failed to create customer:',{status:503})
    expect(form.querySelector('input[type=text]')).toHaveValue('Synthetic')
    expect(screen.getByRole('button',{name:'customerSelectModal.createAndSelect'})).toBeEnabled()
  })
})
